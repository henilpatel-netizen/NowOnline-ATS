using Ats.Application.Users;
using Ats.Domain.Enums;
using Xunit;

namespace Ats.Tests.Users;

public class UserAuditSummaryTests
{
    private static readonly DateTimeOffset Added = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserListItem Before = new(7, "Old Name", "old@acme.test", AtsRole.Viewer, true, Added);

    [Fact]
    public void Lists_every_changed_field_against_the_old_email()
    {
        var after = Before with { DisplayName = "New Name", Email = "new@acme.test", Role = AtsRole.Recruiter };
        Assert.Equal("Updated 'old@acme.test': email to 'new@acme.test', role to Recruiter, name",
            UserAuditSummary.Updated(Before, after, [UserField.Name, UserField.Email, UserField.Role]));
    }

    [Fact]
    public void Lists_only_what_changed()
    {
        var after = Before with { Role = AtsRole.Owner };
        Assert.Equal("Updated 'old@acme.test': role to Owner", UserAuditSummary.Updated(Before, after, [UserField.Role]));
    }

    [Theory]
    [InlineData(1, "Updated 'old@acme.test': role to Viewer, removed from 1 hiring team")]
    [InlineData(3, "Updated 'old@acme.test': role to Viewer, removed from 3 hiring teams")]
    public void Names_the_hiring_teams_a_role_change_removed(int removed, string expected)
    {
        var after = Before with { Role = AtsRole.Viewer };
        Assert.Equal(expected, UserAuditSummary.Updated(Before, after, [UserField.Role], removed));
    }

    [Fact]
    public void A_name_change_does_not_repeat_the_name()
    {
        var after = Before with { DisplayName = "New Name" };
        Assert.Equal("Updated 'old@acme.test': name", UserAuditSummary.Updated(Before, after, [UserField.Name]));
    }
}
