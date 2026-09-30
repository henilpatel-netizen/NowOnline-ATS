using Ats.Application.Candidates;
using Ats.Domain.Entities;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Candidates;

public class CandidateServiceTests
{
    private static (CandidateService Service, FakeCandidateRepository Repo) Build()
    {
        var repo = new FakeCandidateRepository();
        repo.Candidates.AddRange(
            new Candidate { Id = 1, FirstName = "Ada", LastName = "L", Email = "ada@example.test" },
            new Candidate { Id = 2, FirstName = "Bob", LastName = "K", Email = "bob@example.test" });
        repo.Applications.AddRange(
            new JobApplication { Id = 10, CandidateId = 1, JobId = 5 },
            new JobApplication { Id = 11, CandidateId = 1, JobId = 6 },
            new JobApplication { Id = 12, CandidateId = 2, JobId = 5 });
        return (new CandidateService(repo), repo);
    }

    [Fact]
    public async Task Deleting_an_unknown_candidate_fails()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(99);

        Assert.False(result.Succeeded);
        Assert.Equal("Candidate not found.", result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Deleting_a_candidate_soft_deletes_them_and_all_their_applications_in_one_save()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(1);

        Assert.True(result.Succeeded);
        Assert.True(repo.Candidates.Single(c => c.Id == 1).IsDeleted);
        Assert.All(repo.Applications.Where(a => a.CandidateId == 1), a => Assert.True(a.IsDeleted));
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task Deleting_a_candidate_leaves_other_candidates_and_their_applications_alone()
    {
        var (service, repo) = Build();

        await service.DeleteAsync(1);

        Assert.False(repo.Candidates.Single(c => c.Id == 2).IsDeleted);
        Assert.False(repo.Applications.Single(a => a.Id == 12).IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_candidate_during_a_concurrent_board_move_reports_a_friendly_failure()
    {
        var (service, repo) = Build();
        repo.ConcurrencyConflict = true;

        var result = await service.DeleteAsync(1);

        Assert.False(result.Succeeded);
        Assert.Equal("This candidate was changed by someone else. Reload the page and try again.", result.Error);
    }
}
