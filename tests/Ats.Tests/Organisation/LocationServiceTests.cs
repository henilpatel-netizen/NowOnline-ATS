using Ats.Application.Locations;
using Ats.Domain.Entities;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Organisation;

// Locations are hard-deleted, so the in-use guard is the only thing keeping a job's
// location reference valid.
public class LocationServiceTests
{
    private static (LocationService Service, FakeLocationRepository Repo) Build()
    {
        var repo = new FakeLocationRepository();
        repo.Locations.Add(new Location { Id = 4, Name = "Head office", City = "Amsterdam" });
        return (new LocationService(repo), repo);
    }

    [Fact]
    public async Task Deleting_an_unknown_location_fails()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(99);

        Assert.False(result.Succeeded);
        Assert.Equal("Location not found.", result.Error);
        Assert.Single(repo.Locations);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task A_location_used_by_a_job_is_not_deleted()
    {
        var (service, repo) = Build();
        repo.UsedByJob.Add(4);

        var result = await service.DeleteAsync(4);

        Assert.False(result.Succeeded);
        Assert.Equal("This location is used by one or more jobs and cannot be deleted.", result.Error);
        Assert.Single(repo.Locations);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task An_unused_location_is_deleted()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(4);

        Assert.True(result.Succeeded);
        Assert.Empty(repo.Locations);
        Assert.Equal(1, repo.SaveCount);
    }
}
