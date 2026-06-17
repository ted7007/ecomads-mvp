using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ecomads.WebApplication.Services;

namespace Ecomads.WebApplication.Middleware;

public class DemoAccessMiddleware
{
    private const string DemoFeedbackPath = "/demo-feedback";

    private static readonly PathString[] ProductApiPrefixes =
    [
        new("/api/projects"),
        new("/api/statistics"),
        new("/api/recommendations")
    ];

    private static readonly PathString[] ProductAppPrefixes =
    [
        new("/dashboard"),
        new("/campaign"),
        new("/report")
    ];

    private static readonly PathString[] AllowedPrefixes =
    [
        new("/api/auth"),
        new("/api/demo-feedback"),
        new("/login"),
        new("/demo-feedback"),
        new("/assets")
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<DemoAccessMiddleware> _logger;

    public DemoAccessMiddleware(RequestDelegate next, ILogger<DemoAccessMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserAccessService userAccessService)
    {
        if (!ShouldCheckAccess(context))
        {
            await _next(context);
            return;
        }

        var userId = GetUserId(context.User);
        if (!userId.HasValue)
        {
            await _next(context);
            return;
        }

        var shouldRequireFeedback = await userAccessService.ShouldRequireDemoFeedbackAsync(userId.Value);
        if (!shouldRequireFeedback)
        {
            await _next(context);
            return;
        }

        _logger.LogInformation(
            "Redirecting expired demo user {UserId} to {DemoFeedbackPath}. RequestPath: {RequestPath}",
            userId.Value,
            DemoFeedbackPath,
            context.Request.Path.Value);

        if (IsApiRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Демо-доступ закончился. Оставьте обратную связь, чтобы продолжить пользоваться MVP.",
                redirectTo = DemoFeedbackPath
            });
            return;
        }

        context.Response.Redirect(DemoFeedbackPath);
    }

    private static bool ShouldCheckAccess(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var path = context.Request.Path;
        if (IsAllowedPath(path) || HasStaticFileExtension(path))
        {
            return false;
        }

        return ProductApiPrefixes.Any(prefix => path.StartsWithSegments(prefix))
            || ProductAppPrefixes.Any(prefix => path.StartsWithSegments(prefix));
    }

    private static bool IsAllowedPath(PathString path)
    {
        return AllowedPrefixes.Any(prefix => path.StartsWithSegments(prefix))
            || path == "/";
    }

    private static bool HasStaticFileExtension(PathString path)
    {
        var value = path.Value;
        return !string.IsNullOrWhiteSpace(value)
            && Path.HasExtension(value);
    }

    private static bool IsApiRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api");
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var userIdValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdValue, out var userId) ? userId : null;
    }
}
