using System.ComponentModel.DataAnnotations;
using Ecomads.WebApplication.Data;
using Ecomads.WebApplication.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomads.WebApplication.Controllers;

public sealed class PilotApplicationRequest : IValidatableObject
{
    public Guid SubmissionId { get; set; }
    [Required, StringLength(100, MinimumLength = 1)] public string Name { get; set; } = "";
    [Required, StringLength(100)] public string Contact { get; set; } = "";
    [RegularExpression("^(one|two-five|over-five|agency)$")] public string? Stores { get; set; }
    [RegularExpression("^(under-100k|100k-500k|500k-1m|over-1m)$")] public string? Budget { get; set; }
    [RegularExpression("^(yes|no|unknown)$")] public string? Jam { get; set; }
    public bool ContactConsent { get; set; }
    [StringLength(200)] public string? Website { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (SubmissionId == Guid.Empty) yield return new("Нужен идентификатор отправки.", [nameof(SubmissionId)]);
        if (!ContactConsent) yield return new("Подтвердите, что команда может с вами связаться.", [nameof(ContactConsent)]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(Contact?.Trim() ?? "", @"^(@[A-Za-z0-9_]{5,32}|\+?[\d ()-]{7,25})$"))
            yield return new("Укажите Telegram @username или телефон.", [nameof(Contact)]);
    }
}

[ApiController, Route("api/pilot-applications")]
public sealed class PilotApplicationsController(EcomadsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost, AllowAnonymous, Consumes("application/json"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Submit(PilotApplicationRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Website)) return Ok(new { accepted = true });
        var now = clock.GetUtcNow().UtcDateTime;
        // The conflict is resolved inside PostgreSQL, including concurrent/retried requests.
        // Return no contact or record ID from this public endpoint.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pilot_applications
                ("Id", "SubmissionId", "Name", "Contact", "Stores", "Budget", "Jam", "Status", "Note", "CreatedAtUtc", "UpdatedAtUtc", "ContactConsentAtUtc")
            VALUES ({Guid.NewGuid()}, {request.SubmissionId}, {request.Name.Trim()}, {request.Contact.Trim()},
                {request.Stores}, {request.Budget}, {request.Jam}, 'new', '', {now}, {now}, {now})
            ON CONFLICT ("SubmissionId") DO NOTHING
            """, ct);
        return Ok(new { accepted = true });
    }
}

public sealed class PilotApplicationUpdate
{
    [Required, RegularExpression("^(new|contacted|in-pilot|closed|spam)$")] public string Status { get; set; } = "new";
    [Required(AllowEmptyStrings = true), StringLength(2000)] public string Note { get; set; } = "";
}

[ApiController, Authorize, Route("api/admin/pilot-applications")]
public sealed class PilotApplicationsAdminController(EcomadsDbContext db, PilotApplicationAccess access,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, RegularExpression("^(new|contacted|in-pilot|closed|spam)$")] string? status = null,
        CancellationToken ct = default)
    {
        if (!access.CanManage(User)) return Forbid();
        var query = db.PilotApplications.AsNoTracking();
        if (status != null) query = query.Where(x => x.Status == status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * 20, int.MaxValue)).Take(20).ToListAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { items, total, page, pageSize = 20 });
    }

    [HttpPatch("{id:guid}"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Update(Guid id, PilotApplicationUpdate request, CancellationToken ct)
    {
        if (!access.CanManage(User)) return Forbid();
        var item = await db.PilotApplications.FindAsync([id], ct);
        if (item == null) return NotFound();
        item.Status = request.Status;
        item.Note = request.Note.Trim();
        item.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
