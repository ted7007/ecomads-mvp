using System.Security.Claims;
using System.Text.Json;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController]
[Authorize]
[Route("api/wb/norms")]
public sealed class WbNormsController(EcomadsDbContext db) : ControllerBase
{
    public sealed record StoreNormRequest(decimal TargetDrr, int MinClicks, decimal MinSpend,
        int MinOrders, decimal DeviationPercent, decimal MinCtr = 3m);
    public sealed record CampaignNormRequest(string? CustomName, string? Goal, decimal? TargetDrr,
        int? MinClicks, decimal? MinSpend, int? MinOrders, decimal? DeviationPercent, decimal? MinCtr = null);
    public sealed record StoreNormResponse(Guid StoreId, StoreNormRequest Values, int Version, DateTime? UpdatedAtUtc);
    public sealed record CampaignNormResponse(Guid CampaignId, CampaignNormRequest Overrides,
        StoreNormRequest Effective, int Version, int StoreVersion, DateTime? UpdatedAtUtc);
    public sealed record CampaignNormListRow(Guid CampaignId, string WbName, string Name, string? Goal,
        decimal TargetDrr, bool IsInherited);

    [HttpGet("stores/{storeId:guid}/campaigns")]
    public async Task<IActionResult> ListCampaigns(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken)) return NotFound();
        var fallback = await db.WbStoreNorms.AsNoTracking().Where(x => x.StoreId == storeId)
            .Select(x => (decimal?)x.TargetDrr).SingleOrDefaultAsync(cancellationToken) ?? 30m;
        var rows = await db.Campaigns.AsNoTracking().Where(x => x.StoreId == storeId)
            .OrderBy(x => x.Name)
            .Select(x => new CampaignNormListRow(x.Id, x.Name,
                db.WbCampaignNorms.Where(n => n.CampaignId == x.Id).Select(n => n.CustomName).FirstOrDefault() ?? x.Name,
                db.WbCampaignNorms.Where(n => n.CampaignId == x.Id).Select(n => n.Goal).FirstOrDefault(),
                db.WbCampaignNorms.Where(n => n.CampaignId == x.Id).Select(n => n.TargetDrr).FirstOrDefault() ?? fallback,
                !db.WbCampaignNorms.Any(n => n.CampaignId == x.Id && n.TargetDrr != null)))
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpGet("stores/{storeId:guid}")]
    public async Task<IActionResult> GetStore(Guid storeId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken)) return NotFound();
        var norms = await db.WbStoreNorms.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);
        return Ok(ToStoreResponse(storeId, norms));
    }

    [HttpPut("stores/{storeId:guid}")]
    public async Task<IActionResult> PutStore(Guid storeId, [FromBody] StoreNormRequest request, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.SellerId == sellerId, cancellationToken)) return NotFound();
        if (!Valid(request.TargetDrr, request.MinClicks, request.MinSpend, request.MinOrders, request.DeviationPercent) || !ValidCtr(request.MinCtr))
            return BadRequest(new { message = "Нормы должны быть неотрицательными и находиться в допустимых пределах." });

        var norms = await db.WbStoreNorms.SingleOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);
        if (norms == null)
        {
            norms = new WbStoreNorms { StoreId = storeId };
            db.WbStoreNorms.Add(norms);
        }
        else norms.Version++;
        norms.TargetDrr = request.TargetDrr;
        norms.MinCtr = request.MinCtr;
        norms.MinClicks = request.MinClicks;
        norms.MinSpend = request.MinSpend;
        norms.MinOrders = request.MinOrders;
        norms.DeviationPercent = request.DeviationPercent;
        norms.UpdatedAtUtc = DateTime.UtcNow;
        db.WbNormRevisions.Add(new WbNormRevision
        {
            Id = Guid.NewGuid(), StoreId = storeId, Version = norms.Version,
            SettingsJson = JsonSerializer.Serialize(request), CreatedAtUtc = norms.UpdatedAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToStoreResponse(storeId, norms));
    }

    [HttpGet("campaigns/{campaignId:guid}")]
    public async Task<IActionResult> GetCampaign(Guid campaignId, CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var storeId = await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync(cancellationToken);
        if (!storeId.HasValue) return NotFound();
        var storeNorms = await db.WbStoreNorms.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId.Value, cancellationToken);
        var campaignNorms = await db.WbCampaignNorms.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);
        return Ok(ToCampaignResponse(campaignId, storeNorms, campaignNorms));
    }

    [HttpPut("campaigns/{campaignId:guid}")]
    public async Task<IActionResult> PutCampaign(Guid campaignId, [FromBody] CampaignNormRequest request,
        CancellationToken cancellationToken)
    {
        if (!TrySellerId(out var sellerId)) return Unauthorized();
        var storeId = await db.Campaigns.Where(x => x.Id == campaignId && x.Store.SellerId == sellerId)
            .Select(x => (Guid?)x.StoreId).SingleOrDefaultAsync(cancellationToken);
        if (!storeId.HasValue) return NotFound();
        if (request.CustomName?.Length > 255 || request.Goal?.Length > 255 ||
            !Valid(request.TargetDrr, request.MinClicks, request.MinSpend, request.MinOrders, request.DeviationPercent) ||
            request.MinCtr.HasValue && !ValidCtr(request.MinCtr.Value))
            return BadRequest(new { message = "Проверьте длину названия и допустимые значения норм." });

        var norms = await db.WbCampaignNorms.SingleOrDefaultAsync(x => x.CampaignId == campaignId, cancellationToken);
        if (norms == null)
        {
            norms = new WbCampaignNorms { CampaignId = campaignId };
            db.WbCampaignNorms.Add(norms);
        }
        else norms.Version++;
        norms.CustomName = string.IsNullOrWhiteSpace(request.CustomName) ? null : request.CustomName.Trim();
        norms.Goal = string.IsNullOrWhiteSpace(request.Goal) ? null : request.Goal.Trim();
        norms.TargetDrr = request.TargetDrr;
        norms.MinCtr = request.MinCtr;
        norms.MinClicks = request.MinClicks;
        norms.MinSpend = request.MinSpend;
        norms.MinOrders = request.MinOrders;
        norms.DeviationPercent = request.DeviationPercent;
        norms.UpdatedAtUtc = DateTime.UtcNow;
        db.WbNormRevisions.Add(new WbNormRevision
        {
            Id = Guid.NewGuid(), StoreId = storeId.Value, CampaignId = campaignId, Version = norms.Version,
            SettingsJson = JsonSerializer.Serialize(request), CreatedAtUtc = norms.UpdatedAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
        var storeNorms = await db.WbStoreNorms.AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId.Value, cancellationToken);
        return Ok(ToCampaignResponse(campaignId, storeNorms, norms));
    }

    private static StoreNormResponse ToStoreResponse(Guid storeId, WbStoreNorms? norms) =>
        new(storeId, new StoreNormRequest(norms?.TargetDrr ?? 30m, norms?.MinClicks ?? 30,
            norms?.MinSpend ?? 500m, norms?.MinOrders ?? 3, norms?.DeviationPercent ?? 40m, norms?.MinCtr ?? 3m),
            norms?.Version ?? 0, norms?.UpdatedAtUtc);

    private static CampaignNormResponse ToCampaignResponse(Guid campaignId, WbStoreNorms? store, WbCampaignNorms? campaign)
    {
        var defaults = ToStoreResponse(store?.StoreId ?? Guid.Empty, store).Values;
        var overrides = new CampaignNormRequest(campaign?.CustomName, campaign?.Goal, campaign?.TargetDrr,
            campaign?.MinClicks, campaign?.MinSpend, campaign?.MinOrders, campaign?.DeviationPercent, campaign?.MinCtr);
        var effective = new StoreNormRequest(overrides.TargetDrr ?? defaults.TargetDrr,
            overrides.MinClicks ?? defaults.MinClicks, overrides.MinSpend ?? defaults.MinSpend,
            overrides.MinOrders ?? defaults.MinOrders, overrides.DeviationPercent ?? defaults.DeviationPercent,
            overrides.MinCtr ?? defaults.MinCtr);
        return new CampaignNormResponse(campaignId, overrides, effective, campaign?.Version ?? 0,
            store?.Version ?? 0, campaign?.UpdatedAtUtc);
    }

    private static bool Valid(decimal? drr, int? clicks, decimal? spend, int? orders, decimal? deviation) =>
        (drr is null or >= 0 and <= 1000) && (clicks is null or >= 0 and <= 1_000_000) &&
        (spend is null or >= 0 and <= 1_000_000_000) && (orders is null or >= 0 and <= 1_000_000) &&
        (deviation is null or >= 0 and <= 1000);

    private static bool ValidCtr(decimal ctr) => ctr is > 0 and <= 100;

    private bool TrySellerId(out Guid sellerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out sellerId);
}
