using Ats.Application.Abstractions;
using Ats.Application.Applications;
using Ats.Application.Common;
using Ats.Domain.Enums;
using Ats.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Applications;

// Read projection for the candidate drawer / application detail. Reuses RelativeTime for
// days-in-stage and IFileStore.StatAsync for the resume size. All reads; nothing is written.
public sealed class ApplicationCardQuery : IApplicationCardQuery
{
    private readonly AtsDbContext _db;
    private readonly IFileStore _files;
    public ApplicationCardQuery(AtsDbContext db, IFileStore files) { _db = db; _files = files; }

    public async Task<ApplicationCard?> GetAsync(int applicationId, CancellationToken ct = default)
    {
        var a = await _db.Applications
            .Where(x => x.Id == applicationId)
            .Select(x => new
            {
                x.Id,
                x.JobId,
                x.CandidateId,
                x.CurrentStageId,
                x.SourceCode,
                x.Origin,
                x.AppliedAt,
                x.Status,
                JobTitle = _db.Jobs.Where(j => j.Id == x.JobId).Select(j => j.Title).FirstOrDefault(),
                Cand = _db.Candidates.Where(c => c.Id == x.CandidateId)
                    .Select(c => new { c.FirstName, c.LastName, c.Email, c.Phone, c.ResumeFileKey }).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (a is null || a.Cand is null) return null;

        var stages = await _db.PipelineStages
            .Where(s => _db.Jobs.Any(j => j.Id == a.JobId && j.PipelineTemplateId == s.PipelineTemplateId))
            .OrderBy(s => s.Order)
            .Select(s => new { s.Id, s.Name, s.Order, s.IsTerminal })
            .ToListAsync(ct);

        var currentIndex = stages.FindIndex(s => s.Id == a.CurrentStageId);
        var progress = stages
            .Where(s => !s.IsTerminal)
            .Select(s => new StageProgressItem(
                s.Name,
                currentIndex >= 0 && s.Order <= stages[currentIndex].Order,
                s.Id == a.CurrentStageId))
            .ToList();
        var nextStage = currentIndex >= 0 && currentIndex + 1 < stages.Count ? stages[currentIndex + 1].Name : null;

        var lastEvent = await _db.ApplicationEvents
            .Where(e => e.ApplicationId == a.Id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(ct);
        var daysInStage = RelativeTime.WholeDays(lastEvent ?? a.AppliedAt, DateTimeOffset.UtcNow);

        var deliveryState = await _db.OutboxMessages
            .Where(m => m.ApplicationId == a.Id)
            .OrderByDescending(m => m.Id)
            .Select(m => (OutboxStatus?)m.Status)
            .FirstOrDefaultAsync(ct);

        var events = await _db.ApplicationEvents
            .Where(e => e.ApplicationId == a.Id)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new { e.FromStageId, e.ToStageId, e.OccurredAt })
            .ToListAsync(ct);
        string StageName(int id) => stages.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        var history = events.Select((e, i) => new ApplicationHistoryItem(
            e.FromStageId is null ? $"Applied — {StageName(e.ToStageId)}" : $"Moved to {StageName(e.ToStageId)}",
            e.OccurredAt,
            i == 0)).ToList();

        StoredFileInfo? file = a.Cand.ResumeFileKey is null ? null : await _files.StatAsync(a.Cand.ResumeFileKey, ct);

        return new ApplicationCard(
            a.Id, $"{a.Cand.FirstName} {a.Cand.LastName}", a.Cand.Email, a.Cand.Phone,
            a.JobTitle ?? "(job)", a.JobId, StageName(a.CurrentStageId), nextStage,
            a.Status, a.Origin, a.SourceCode, a.AppliedAt, daysInStage,
            deliveryState?.ToString(),
            file?.FileName, file?.Length,
            progress, history);
    }
}
