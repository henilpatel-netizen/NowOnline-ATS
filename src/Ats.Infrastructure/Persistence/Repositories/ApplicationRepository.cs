using Ats.Application.Applications;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class ApplicationRepository : IApplicationRepository
{
    private readonly AtsDbContext _db;
    public ApplicationRepository(AtsDbContext db) => _db = db;

    public Task<Job?> GetJobAsync(int jobId, CancellationToken ct = default) =>
        _db.Jobs.Include(j => j.Department).Include(j => j.Location)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<Dictionary<int, DateTimeOffset>> LatestEventTimesForJobAsync(int jobId, CancellationToken ct = default) =>
        await (
            from e in _db.ApplicationEvents
            join a in _db.Applications on e.ApplicationId equals a.Id
            where a.JobId == jobId
            group e by e.ApplicationId into g
            select new { ApplicationId = g.Key, Last = g.Max(x => x.OccurredAt) })
            .ToDictionaryAsync(x => x.ApplicationId, x => x.Last, ct);

    public async Task<List<PipelineStage>> GetStagesForJobAsync(int jobId, CancellationToken ct = default)
    {
        var templateId = await _db.Jobs.Where(j => j.Id == jobId)
            .Select(j => (int?)j.PipelineTemplateId).FirstOrDefaultAsync(ct);
        if (templateId is null) return new List<PipelineStage>();
        return await _db.PipelineStages.Where(s => s.PipelineTemplateId == templateId.Value)
            .OrderBy(s => s.Order).ToListAsync(ct);
    }

    public Task<List<JobApplication>> ListForJobAsync(int jobId, CancellationToken ct = default) =>
        _db.Applications.Include(a => a.Candidate)
            .Where(a => a.JobId == jobId)
            .OrderBy(a => a.AppliedAt).ToListAsync(ct);

    public Task<JobApplication?> GetAsync(int id, CancellationToken ct = default) =>
        _db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<JobApplication?> FindByCandidateJobAsync(int candidateId, int jobId, CancellationToken ct = default) =>
        _db.Applications.FirstOrDefaultAsync(a => a.CandidateId == candidateId && a.JobId == jobId, ct);

    public async Task AddApplicationAsync(JobApplication application, CancellationToken ct = default) =>
        await _db.Applications.AddAsync(application, ct);

    public async Task AddEventAsync(ApplicationEvent ev, CancellationToken ct = default) =>
        await _db.ApplicationEvents.AddAsync(ev, ct);

    public Task<List<ApplicationEvent>> ListEventsAsync(int applicationId, CancellationToken ct = default) =>
        _db.ApplicationEvents.Where(e => e.ApplicationId == applicationId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync(ct);

    public void SetExpectedRowVersion(JobApplication application, byte[] rowVersion) =>
        _db.Entry(application).Property(a => a.RowVersion).OriginalValue = rowVersion;

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
