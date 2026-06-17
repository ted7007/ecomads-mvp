using System.Security.Claims;
using Ecomads.WebApplication.Controllers;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services;
using Ecomads.WebApplication.Services.Analytics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecomads.WebApplication.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class UserAccessAndFeedbackTests
{
    private readonly PostgresFixture _postgres;

    public UserAccessAndFeedbackTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task RegularUserHasProductAccess()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateRegularSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var service = CreateUserAccessService(dbContext);

        Assert.True(await service.HasProductAccessAsync(seller.Id));
    }

    [Fact]
    public async Task ActiveDemoUserHasProductAccess()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateActiveDemoSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var service = CreateUserAccessService(dbContext);

        var accessState = await service.GetAccessStateAsync(seller.Id);

        Assert.True(accessState.HasProductAccess);
        Assert.False(accessState.ShouldRequireDemoFeedback);
        Assert.False(await service.ShouldRequireDemoFeedbackAsync(seller.Id));
    }

    [Fact]
    public async Task ExpiredDemoUserRequiresFeedbackAndLosesProductAccess()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateExpiredDemoSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var service = CreateUserAccessService(dbContext);

        var accessState = await service.GetAccessStateAsync(seller.Id);

        Assert.False(accessState.HasProductAccess);
        Assert.True(accessState.ShouldRequireDemoFeedback);
        Assert.Equal(DemoAccessStatus.ExpiredAwaitingFeedback, accessState.DemoStatus);
    }

    [Fact]
    public async Task DemoFeedbackValidationRejectsShortComment()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateExpiredDemoSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var controller = CreateFeedbackController(dbContext, seller.Id);

        var result = await controller.Submit(CreateFeedbackRequest(generalComment: "short"));

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Empty(await dbContext.DemoFeedbacks.ToListAsync());
    }

    [Fact]
    public async Task ValidFeedbackIsSavedOnceAndGrantsMvpAccess()
    {
        await using var dbContext = await _postgres.CreateMigratedDbContextAsync();
        var seller = TestData.CreateExpiredDemoSeller();
        dbContext.Sellers.Add(seller);
        await dbContext.SaveChangesAsync();

        var controller = CreateFeedbackController(dbContext, seller.Id);

        var firstResult = await controller.Submit(CreateFeedbackRequest());
        var secondResult = await controller.Submit(CreateFeedbackRequest());

        Assert.IsType<OkObjectResult>(firstResult);
        Assert.IsType<ConflictObjectResult>(secondResult);
        Assert.Equal(1, await dbContext.DemoFeedbacks.CountAsync(item => item.UserId == seller.Id));

        var updatedSeller = await dbContext.Sellers.SingleAsync(item => item.Id == seller.Id);
        Assert.Equal(UserAccessType.MvpAccess, updatedSeller.AccessType);
        Assert.Equal(DemoAccessStatus.FeedbackSubmitted, updatedSeller.DemoStatus);
        Assert.NotNull(updatedSeller.DemoFeedbackSubmittedAtUtc);
        Assert.NotNull(updatedSeller.MvpAccessGrantedAtUtc);

        var feedback = await dbContext.DemoFeedbacks.SingleAsync(item => item.UserId == seller.Id);
        Assert.Equal("keyword_recommendations", feedback.MostUsefulFeature);
        Assert.Contains("statistics_upload", feedback.WrongOrQuestionableRecommendations);
        Assert.Contains("reduce_drr", feedback.MissingForRegularUsage);

        Assert.True(await dbContext.ProductUsageEvents.AnyAsync(item =>
            item.UserId == seller.Id &&
            item.EventName == ProductEvents.DemoFeedbackSubmitted));
    }

    private static IUserAccessService CreateUserAccessService(Ecomads.WebApplication.Data.EcomadsDbContext dbContext)
    {
        return new UserAccessService(dbContext, NullLogger<UserAccessService>.Instance);
    }

    private static DemoFeedbackController CreateFeedbackController(
        Ecomads.WebApplication.Data.EcomadsDbContext dbContext,
        Guid userId)
    {
        var userAccessService = CreateUserAccessService(dbContext);
        var analyticsService = new ProductAnalyticsService(dbContext, NullLogger<ProductAnalyticsService>.Instance);
        var controller = new DemoFeedbackController(
            dbContext,
            userAccessService,
            analyticsService,
            NullLogger<DemoFeedbackController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(userId)
        };

        return controller;
    }

    private static DefaultHttpContext CreateHttpContext(Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "test"))
        };
    }

    private static DemoFeedbackSubmitRequest CreateFeedbackRequest(string? generalComment = null)
    {
        return new DemoFeedbackSubmitRequest
        {
            PrimaryTask = "reduce_drr",
            UsedSections = ["statistics_upload", "keyword_recommendations"],
            MostUsefulFeature = "keyword_recommendations",
            RecommendationsClarityScore = 4,
            MissingForDecision = ["more_recommendation_explanations", "money_effect_forecast"],
            GeneralComment = generalComment ??
                "Подробный комментарий для теста demo feedback: обзор понятен, рекомендации полезны, но нужны уточнения.",
            ContinueUsingAnswer = "maybe_after_improvements",
            ImprovementPriority = "Нужны экспорт и история изменений."
        };
    }
}
