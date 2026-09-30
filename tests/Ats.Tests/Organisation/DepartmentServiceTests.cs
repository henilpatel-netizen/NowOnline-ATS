using Ats.Application.Departments;
using Ats.Domain.Entities;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Organisation;

// Departments are hard-deleted, so the in-use guard is the only thing keeping a job's
// department reference valid.
public class DepartmentServiceTests
{
    private static (DepartmentService Service, FakeDepartmentRepository Repo) Build()
    {
        var repo = new FakeDepartmentRepository();
        repo.Departments.Add(new Department { Id = 7, Name = "Engineering" });
        return (new DepartmentService(repo), repo);
    }

    [Fact]
    public async Task Deleting_an_unknown_department_fails()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(99);

        Assert.False(result.Succeeded);
        Assert.Equal("Department not found.", result.Error);
        Assert.Single(repo.Departments);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task A_department_used_by_a_job_is_not_deleted()
    {
        var (service, repo) = Build();
        repo.UsedByJob.Add(7);

        var result = await service.DeleteAsync(7);

        Assert.False(result.Succeeded);
        Assert.Equal("This department is used by one or more jobs and cannot be deleted.", result.Error);
        Assert.Single(repo.Departments);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task An_unused_department_is_deleted()
    {
        var (service, repo) = Build();

        var result = await service.DeleteAsync(7);

        Assert.True(result.Succeeded);
        Assert.Empty(repo.Departments);
        Assert.Equal(1, repo.SaveCount);
    }
}
