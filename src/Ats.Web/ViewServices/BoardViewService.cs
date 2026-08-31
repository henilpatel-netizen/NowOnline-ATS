using Ats.Application.Applications;
using Ats.Application.Candidates;
using Ats.Application.Common;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Web.Models;
using Ats.Web.Models.Board;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Ats.Web.ViewServices;

public interface IBoardViewService
{
    // Null when the job does not exist (or is not visible to this tenant).
    Task<BoardViewModel?> BuildAsync(int jobId, string? error, CancellationToken ct = default);
}

// The board's aggregation used to sit inline in BoardController, the one screen that broke the
// "controllers stay thin" rule (QUAL-3). Behaviour is unchanged: this is the same computation, moved
// somewhere injectable and testable.
public sealed class BoardViewService : IBoardViewService
{
    private readonly IApplicationService _applications;
    private readonly ICandidateService _candidates;

    public BoardViewService(IApplicationService applications, ICandidateService candidates)
    {
        _applications = applications;
        _candidates = candidates;
    }

    public async Task<BoardViewModel?> BuildAsync(int jobId, string? error, CancellationToken ct = default)
    {
        var job = await _applications.GetJobAsync(jobId, ct);
        if (job is null) return null;

        var stages = (await _applications.GetStagesForJobAsync(jobId, ct)).OrderBy(s => s.Order).ToList();
        var apps = await _applications.ListForJobAsync(jobId, ct);
        var lastEvents = await _applications.LatestEventTimesForJobAsync(jobId, ct);
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset LastActivity(JobApplication a) =>
            lastEvents.TryGetValue(a.Id, out var t) ? t : a.AppliedAt;
        int DaysInStage(JobApplication a) => RelativeTime.WholeDays(LastActivity(a), now);

        var columns = stages.Select((stage, index) => new BoardColumn(stage, index, stages.Count,
            apps.Where(a => a.CurrentStageId == stage.Id)
                .Select(a => new BoardCardModel(
                    a.Id,
                    a.Candidate?.FullName ?? "(unknown)",
                    a.Candidate?.Email ?? "",
                    Convert.ToBase64String(a.RowVersion),
                    a.Origin,
                    DaysInStage(a),
                    index,
                    stages.Count,
                    stage.IsTerminal && stage.TerminalOutcome == StageOutcome.Rejected))
                .ToList())).ToList();

        var active = apps.Where(a => a.Status == ApplicationStatus.Active).ToList();
        var candidateOptions = (await _candidates.ListAsync(ct))
            .Select(c => new SelectListItem($"{c.FullName} <{c.Email}>", c.Id.ToString()))
            .ToList();

        return new BoardViewModel
        {
            Job = job,
            Columns = columns,
            Error = error,
            CandidateOptions = candidateOptions,
            InProcess = active.Count,
            AvgDaysInStage = DashboardMath.MeanDays(active.Select(a => now - LastActivity(a)).ToList()),
            FromReferral = apps.Count(a => a.Origin == ApplicationOrigin.Referral),
            OldestDays = apps.Count == 0 ? 0 : apps.Max(a => RelativeTime.WholeDays(a.AppliedAt, now))
        };
    }
}
