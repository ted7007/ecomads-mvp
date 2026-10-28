using System.Security.Claims;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Data.Models;
using Ecomads.WebApplication.Services.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

[ApiController, Authorize, Route("api/wb/campaigns/{campaignId:guid}/recommendations/runs/{runId:guid}/decisions")]
public sealed class WbSnapshotDecisionsController(EcomadsDbContext db, RecommendationService service, ResultsService? results = null) : ControllerBase
{
    public sealed record DecisionRequest(IReadOnlyList<Guid> EvaluationIds, string RuleId, string Status,
        DateOnly? SnoozeUntil = null, string? Reason = null, string? Comment = null, string? ReasonCode = null);
    [HttpPost]
    public async Task<IActionResult> Save(Guid campaignId, Guid runId, DecisionRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var seller)) return Unauthorized();
        var autoReplenishment = request.ReasonCode == CampaignRecommendationSettings.AutoReplenishmentReason;
        if (request.EvaluationIds is null || request.EvaluationIds.Count is < 1 or > 1000 ||
            request.Status is not ("accepted" or "snoozed" or "irrelevant") || request.RuleId is not ("R01" or "R02" or "R03" or "R06" or "R07") ||
            request.Status == "snoozed" && (!request.SnoozeUntil.HasValue || request.SnoozeUntil <= Services.Wb.WbSyncPlanner.Today()) ||
            request.ReasonCode != null && (!autoReplenishment || request.RuleId != "R02" || request.Status != "irrelevant") ||
            request.Status == "irrelevant" && !autoReplenishment && string.IsNullOrWhiteSpace(request.Reason) || request.Reason?.Length > 1000 || request.Comment?.Length > 2000)
            return BadRequest(new { message = "Для откладывания нужна будущая дата, для отказа — причина. Проверьте решение." });
        // Validate ownership and IDs against immutable evidence, including an already hidden card.
        var report = await service.SnapshotAsync(seller, campaignId, runId, ct, applyPresentation: false);
        if (report == null) return NotFound();
        if (report.SchemaVersion < 2) return BadRequest(new { message = "Откройте новый расчёт: старый снимок не содержит конкретного действия." });
        var ids = request.EvaluationIds.Distinct().ToArray();
        if (ids.Any(id => !report.Cards.Any(c => c.EvaluationId == id && c.Rule.RuleId == request.RuleId))) return NotFound();
        if (results != null) await results.RegisterAsync(seller, ids.Select(id => new ResultExposureRequest(runId, id, request.RuleId)).ToArray(), ct);
        if (autoReplenishment)
        {
            var settings = await CampaignRecommendationSettings.GetOrCreateAsync(db, campaignId, ct);
            CampaignRecommendationSettings.SetAutoReplenishment(settings, true);
            CampaignRecommendationSettings.RecordRevision(db, report.Cards[0].Subject.StoreId, settings);
        }
        // One SaveChanges transaction commits decisions, preference and revision together.
        foreach (var id in ids) db.WbSnapshotDecisions.Add(new WbSnapshotDecision { Id = Guid.NewGuid(), RunId = runId,
            EvaluationId = id, RuleId = request.RuleId, Status = request.Status, SnoozeUntil = request.Status == "snoozed" ? request.SnoozeUntil : null,
            Reason = request.Status == "irrelevant" ? autoReplenishment ? CampaignRecommendationSettings.AutoReplenishmentLabel : request.Reason!.Trim() : null,
            ReasonCode = request.ReasonCode, Comment = request.Comment?.Trim() });
        await db.SaveChangesAsync(ct);
        return Ok(await service.SnapshotAsync(seller, campaignId, runId, ct));
    }
}
