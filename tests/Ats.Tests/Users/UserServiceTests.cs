using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Application.Users;
using Ats.Domain.Entities;
using Ats.Domain.Enums;
using Ats.Tests.Fakes;
using Xunit;

namespace Ats.Tests.Users;

// These rules decide who can act in a tenant: a wrong one locks a customer out (no Owner left) or
// lets a user keep access after it was taken away (stamp not rotated).
public class UserServiceTests
{
    private const string Temp = "temporary-pass-1";
    private const int OwnerId = 1;

    private static (UserService Service, FakeUserRepository Repo, FakeEmails Emails) Build()
    {
        var repo = new FakeUserRepository();
        repo.Users.Add(new AppUser { Id = OwnerId, Email = "owner@acme.test", DisplayName = "Owner", Role = AtsRole.Owner, PasswordHash = "hash:owner-password-1" });
        repo.Users.Add(new AppUser { Id = 2, Email = "rec@acme.test", DisplayName = "Rec", Role = AtsRole.Recruiter, PasswordHash = "hash:x" });
        var emails = new FakeEmails();
        return (new UserService(repo, emails, new FakeHasher()), repo, emails);
    }

    private static CreateUserInput Input(string role = AtsRole.Viewer, string email = " New.User@Acme.test ", string password = Temp) =>
        new("New User", email, role, password);

    [Fact]
    public async Task Create_adds_an_active_user_who_must_change_password()
    {
        var (s, repo, emails) = Build();
        var (result, userId) = await s.CreateAsync(Input());
        Assert.True(result.Succeeded, result.Error);
        var u = repo.Users.Single(x => x.Email == "new.user@acme.test");
        Assert.Equal(u.Id, userId);
        Assert.True(u.IsActive);
        Assert.True(u.MustChangePassword);
        Assert.Equal(AtsRole.Viewer, u.Role);
        Assert.Equal("hash:" + Temp, u.PasswordHash);
        Assert.Equal("new.user@acme.test", emails.Checked);
    }

    [Fact]
    public async Task Create_accepts_HiringManager()
    {
        var (s, repo, _) = Build();
        var (result, userId) = await s.CreateAsync(Input(AtsRole.HiringManager));
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(AtsRole.HiringManager, repo.Users.Single(u => u.Id == userId).Role);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("owner")]
    [InlineData("")]
    public async Task Create_rejects_unknown_roles(string role) =>
        Assert.False((await Build().Service.CreateAsync(Input(role))).Result.Succeeded);

    [Fact]
    public async Task Create_rejects_an_email_registered_in_any_tenant()
    {
        var (s, repo, emails) = Build();
        emails.Taken = true;
        var (result, _) = await s.CreateAsync(Input());
        Assert.Equal("That email address is already registered.", result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Create_reports_a_duplicate_email_lost_to_a_concurrent_create()
    {
        var (s, repo, _) = Build();
        repo.RejectNextAddAsDuplicate = true;
        var (result, userId) = await s.CreateAsync(Input());
        Assert.Equal("That email address is already registered.", result.Error);
        Assert.Null(userId);
        Assert.Equal(2, repo.Users.Count);
    }

    [Fact]
    public async Task Create_rejects_a_name_over_200_characters() =>
        Assert.False((await Build().Service.CreateAsync(new CreateUserInput(new string('a', 201), "a@acme.test", AtsRole.Viewer, Temp))).Result.Succeeded);

    [Fact]
    public async Task Create_rejects_a_password_over_128_characters() =>
        Assert.Equal("Password must be 128 characters or fewer.",
            (await Build().Service.CreateAsync(Input(password: new string('p', 129)))).Result.Error);

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Create_rejects_invalid_email(string email) =>
        Assert.False((await Build().Service.CreateAsync(Input(email: email))).Result.Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_password(string password) =>
        Assert.Equal("Enter a password.", (await Build().Service.CreateAsync(Input(password: password))).Result.Error);

    // No minimum length for now (decision 1 October 2026); to be reinstated later.
    [Fact]
    public async Task Create_accepts_a_one_character_password()
    {
        var (result, _) = await Build().Service.CreateAsync(Input(password: "p"));
        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name() =>
        Assert.False((await Build().Service.CreateAsync(new CreateUserInput("  ", "a@acme.test", AtsRole.Viewer, Temp))).Result.Succeeded);

    private static UpdateUserInput Edit(int id = 2, string name = "Rec", string email = "rec@acme.test", string role = AtsRole.Recruiter) =>
        new(id, name, email, role);

    [Fact]
    public async Task Update_with_nothing_changed_succeeds_without_saving_or_rotating_the_stamp()
    {
        var (s, repo, emails) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Empty(update.ChangedFields);
        Assert.False(update.SignedOut);
        Assert.Equal(0, repo.SaveCount);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
        Assert.Null(emails.Checked);
    }

    private static (UserService Service, FakeUserRepository Repo) BuildWithHiringManager()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Email = "hm@acme.test", DisplayName = "Hm", Role = AtsRole.HiringManager, PasswordHash = "hash:x" });
        repo.TeamLinks.AddRange(
        [
            new JobHiringManager { JobId = 10, UserId = 3 },
            new JobHiringManager { JobId = 11, UserId = 3 },
            new JobHiringManager { JobId = 10, UserId = 4 },
        ]);
        return (s, repo);
    }

    [Fact]
    public async Task Changing_a_hiring_manager_to_another_role_removes_their_team_links()
    {
        var (s, repo) = BuildWithHiringManager();
        var update = await s.UpdateAsync(Edit(3, "Hm", "hm@acme.test", AtsRole.Viewer), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(2, update.RemovedFromJobs);
        Assert.Equal(4, Assert.Single(repo.TeamLinks).UserId);
        Assert.False(repo.RemovedTeamLinksOutsideTransaction);
    }

    [Fact]
    public async Task A_hiring_manager_keeping_their_role_keeps_their_team_links()
    {
        var (s, repo) = BuildWithHiringManager();
        var update = await s.UpdateAsync(Edit(3, "Renamed", "hm@acme.test", AtsRole.HiringManager), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(0, update.RemovedFromJobs);
        Assert.Equal(3, repo.TeamLinks.Count);
    }

    [Fact]
    public async Task A_failed_role_change_away_from_hiring_manager_keeps_their_team_links()
    {
        var (s, repo) = BuildWithHiringManager();
        var update = await s.UpdateAsync(Edit(3, "Hm", "hm@acme.test", "SuperAdmin"), OwnerId);
        Assert.False(update.Result.Succeeded);
        Assert.Equal(0, update.RemovedFromJobs);
        Assert.Equal(3, repo.TeamLinks.Count);
    }

    [Fact]
    public async Task Deactivating_a_hiring_manager_keeps_their_team_links()
    {
        var (s, repo) = BuildWithHiringManager();
        Assert.True((await s.SetActiveAsync(3, false, OwnerId)).Result.Succeeded);
        Assert.Equal(3, repo.TeamLinks.Count);
    }

    [Fact]
    public async Task Update_changing_the_role_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(role: AtsRole.Viewer), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(AtsRole.Viewer, repo.Users[1].Role);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
        Assert.Equal(new[] { UserField.Role }, update.ChangedFields);
        Assert.True(update.SignedOut);
    }

    [Fact]
    public async Task Update_changing_the_email_normalises_it_and_rotates_the_stamp()
    {
        var (s, repo, emails) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(email: " New.Rec@Acme.test "), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("new.rec@acme.test", repo.Users[1].Email);
        Assert.Equal("new.rec@acme.test", emails.Checked);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
        Assert.Equal(new[] { UserField.Email }, update.ChangedFields);
        Assert.True(update.SignedOut);
    }

    [Fact]
    public async Task Update_changing_only_the_name_saves_without_rotating_the_stamp()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(name: "  Rec Renamed  "), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("Rec Renamed", repo.Users[1].DisplayName);
        Assert.Equal(1, repo.SaveCount);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
        Assert.Equal(new[] { UserField.Name }, update.ChangedFields);
        Assert.False(update.SignedOut);
    }

    [Fact]
    public async Task Update_reports_every_changed_field()
    {
        var (s, _, _) = Build();
        var update = await s.UpdateAsync(new UpdateUserInput(2, "Other", "other@acme.test", AtsRole.Viewer), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(new[] { UserField.Name, UserField.Email, UserField.Role }, update.ChangedFields);
    }

    [Fact]
    public async Task Update_treats_an_email_differing_only_in_case_or_whitespace_as_unchanged()
    {
        var (s, repo, emails) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(email: "  REC@Acme.Test "), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Empty(update.ChangedFields);
        Assert.Equal(0, repo.SaveCount);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
        Assert.Null(emails.Checked);
    }

    // The last-Owner check is only race-safe inside the repository's serializable transaction.
    [Fact]
    public async Task Update_runs_inside_one_transaction()
    {
        var (s, repo, _) = Build();
        Assert.True((await s.UpdateAsync(Edit(role: AtsRole.Viewer), OwnerId)).Result.Succeeded);
        Assert.Equal(1, repo.TransactionCount);
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task A_user_can_change_their_own_name()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[0].SecurityStamp;
        var update = await s.UpdateAsync(Edit(OwnerId, "Owner Renamed", " Owner@acme.test", AtsRole.Owner), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("Owner Renamed", repo.Users[0].DisplayName);
        Assert.Equal(before, repo.Users[0].SecurityStamp);
        Assert.False(update.SignedOut);
    }

    [Fact]
    public async Task A_user_cannot_change_their_own_email()
    {
        var (s, repo, _) = Build();
        var update = await s.UpdateAsync(Edit(OwnerId, "Owner Renamed", "me@acme.test", AtsRole.Owner), OwnerId);
        Assert.Equal("You can only change your own name here.", update.Result.Error);
        Assert.Empty(update.ChangedFields);
        Assert.Equal("owner@acme.test", repo.Users[0].Email);
        Assert.Equal("Owner", repo.Users[0].DisplayName);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task A_user_cannot_change_their_own_role()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2" });
        var update = await s.UpdateAsync(Edit(OwnerId, "Owner", "owner@acme.test", AtsRole.Viewer), OwnerId);
        Assert.Equal("You can only change your own name here.", update.Result.Error);
        Assert.Equal(AtsRole.Owner, repo.Users[0].Role);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Update_rejects_an_email_registered_in_any_tenant()
    {
        var (s, repo, emails) = Build();
        emails.Taken = true;
        var update = await s.UpdateAsync(Edit(email: "taken@acme.test"), OwnerId);
        Assert.Equal("That email address is already registered.", update.Result.Error);
        Assert.Equal("rec@acme.test", repo.Users[1].Email);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Update_reports_a_duplicate_email_lost_to_a_concurrent_change()
    {
        var (s, repo, _) = Build();
        repo.RejectNextSaveAsDuplicate = true;
        var update = await s.UpdateAsync(Edit(email: "taken@acme.test"), OwnerId);
        Assert.Equal("That email address is already registered.", update.Result.Error);
        Assert.Empty(update.ChangedFields);
        Assert.False(update.SignedOut);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Updating_an_unknown_user_fails() =>
        Assert.Equal("User not found.", (await Build().Service.UpdateAsync(Edit(99), OwnerId)).Result.Error);

    [Fact]
    public async Task The_last_active_Owner_cannot_be_demoted()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", IsActive = false });
        var update = await s.UpdateAsync(Edit(OwnerId, "Owner", "owner@acme.test", AtsRole.Viewer), actingUserId: 3);
        Assert.Equal("A workspace needs at least one active Owner.", update.Result.Error);
        Assert.Equal(AtsRole.Owner, repo.Users[0].Role);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task An_Owner_can_be_demoted_when_another_active_Owner_exists()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2" });
        Assert.True((await s.UpdateAsync(Edit(3, "O2", "o2@acme.test", AtsRole.Recruiter), OwnerId)).Result.Succeeded);
        Assert.Equal(AtsRole.Recruiter, repo.Users[2].Role);
    }

    [Fact]
    public async Task An_inactive_Owner_can_be_demoted_while_another_active_Owner_exists()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", IsActive = false });
        Assert.True((await s.UpdateAsync(Edit(3, "O2", "o2@acme.test", AtsRole.Viewer), OwnerId)).Result.Succeeded);
        Assert.Equal(AtsRole.Viewer, repo.Users[2].Role);
    }

    [Fact]
    public async Task Role_can_be_changed_to_HiringManager()
    {
        var (s, repo, _) = Build();
        var update = await s.UpdateAsync(Edit(role: AtsRole.HiringManager), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(AtsRole.HiringManager, repo.Users[1].Role);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("owner")]
    [InlineData("")]
    public async Task Update_rejects_unknown_roles(string role) =>
        Assert.Equal("Choose a valid role.", (await Build().Service.UpdateAsync(Edit(role: role), OwnerId)).Result.Error);

    // A role outside Assignable can only come from the database; keeping it must not block other edits.
    // Every AtsRole is assignable now, so a stored value outside AtsRole.All stands in for one.
    [Fact]
    public async Task A_stored_non_assignable_role_that_is_kept_is_accepted()
    {
        const string legacy = "LegacyRole";
        var (s, repo, _) = Build();
        repo.Users[1].Role = legacy;
        var update = await s.UpdateAsync(Edit(name: "Legacy User", role: legacy), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal(legacy, repo.Users[1].Role);
        Assert.Equal(new[] { UserField.Name }, update.ChangedFields);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task Update_rejects_a_blank_name(string? name) =>
        Assert.Equal("Name is required.", (await Build().Service.UpdateAsync(Edit(name: name!), OwnerId)).Result.Error);

    [Fact]
    public async Task Update_rejects_a_name_over_200_characters() =>
        Assert.Equal("Name must be 200 characters or fewer.",
            (await Build().Service.UpdateAsync(Edit(name: new string('a', 201)), OwnerId)).Result.Error);

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Update_rejects_an_invalid_email(string email)
    {
        var (s, repo, _) = Build();
        Assert.Equal("Enter a valid email address.", (await s.UpdateAsync(Edit(email: email), OwnerId)).Result.Error);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Deactivating_runs_inside_a_transaction()
    {
        var (s, repo, _) = Build();
        Assert.True((await s.SetActiveAsync(2, false, OwnerId)).Result.Succeeded);
        Assert.Equal(1, repo.TransactionCount);
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task Setting_active_on_an_unknown_user_fails() =>
        Assert.Equal("User not found.", (await Build().Service.SetActiveAsync(99, false, OwnerId)).Result.Error);

    [Fact]
    public async Task Deactivating_rotates_the_stamp_and_blocks_the_user()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        Assert.True((await s.SetActiveAsync(2, false, OwnerId)).Result.Succeeded);
        Assert.False(repo.Users[1].IsActive);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task A_user_cannot_deactivate_themselves() =>
        Assert.False((await Build().Service.SetActiveAsync(OwnerId, false, OwnerId)).Result.Succeeded);

    [Fact]
    public async Task The_last_active_Owner_cannot_be_deactivated()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Recruiter, Email = "r2@acme.test", DisplayName = "R2" });
        Assert.False((await s.SetActiveAsync(OwnerId, false, actingUserId: 3)).Result.Succeeded);
        Assert.True(repo.Users[0].IsActive);
    }

    [Fact]
    public async Task Reactivating_restores_access()
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = false;
        var before = repo.Users[1].SecurityStamp;
        Assert.True((await s.SetActiveAsync(2, true, OwnerId)).Result.Succeeded);
        Assert.True(repo.Users[1].IsActive);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changing_the_active_state_reports_a_change(bool active)
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = !active;
        var result = await s.SetActiveAsync(2, active, OwnerId);
        Assert.True(result.Result.Succeeded);
        Assert.True(result.Changed);
    }

    // The caller audits and announces only a real change, so a repeated click must report none.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Setting_the_state_a_user_already_has_changes_nothing(bool active)
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = active;
        var before = repo.Users[1].SecurityStamp;
        var result = await s.SetActiveAsync(2, active, OwnerId);
        Assert.True(result.Result.Succeeded);
        Assert.False(result.Changed);
        Assert.Equal(0, repo.SaveCount);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task A_failed_state_change_reports_no_change() =>
        Assert.False((await Build().Service.SetActiveAsync(99, false, OwnerId)).Changed);

    [Fact]
    public async Task Admin_reset_sets_a_temporary_password_and_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        Assert.True((await s.ResetPasswordAsync(2, Temp, OwnerId)).Succeeded);
        Assert.Equal("hash:" + Temp, repo.Users[1].PasswordHash);
        Assert.True(repo.Users[1].MustChangePassword);
        Assert.NotEqual(before, repo.Users[1].SecurityStamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Admin_reset_rejects_a_blank_password(string password)
    {
        var (s, repo, _) = Build();
        Assert.Equal("Enter a password.", (await s.ResetPasswordAsync(2, password, OwnerId)).Error);
        Assert.Equal("hash:x", repo.Users[1].PasswordHash);
    }

    [Fact]
    public async Task Admin_reset_rejects_a_password_over_128_characters()
    {
        var (s, repo, _) = Build();
        Assert.Equal("Password must be 128 characters or fewer.", (await s.ResetPasswordAsync(2, new string('p', 129), OwnerId)).Error);
        Assert.Equal("hash:x", repo.Users[1].PasswordHash);
    }

    [Fact]
    public async Task Admin_reset_accepts_a_one_character_password()
    {
        var (s, repo, _) = Build();
        var result = await s.ResetPasswordAsync(2, "p", OwnerId);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("hash:p", repo.Users[1].PasswordHash);
    }

    [Fact]
    public async Task Admin_reset_of_an_unknown_user_fails() =>
        Assert.Equal("User not found.", (await Build().Service.ResetPasswordAsync(99, Temp, OwnerId)).Error);

    [Fact]
    public async Task Admin_reset_of_a_deactivated_user_is_refused()
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = false;
        var before = repo.Users[1].SecurityStamp;
        Assert.Equal("Reactivate the user first.", (await s.ResetPasswordAsync(2, Temp, OwnerId)).Error);
        Assert.Equal("hash:x", repo.Users[1].PasswordHash);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
    }

    [Fact]
    public async Task Admin_reset_of_own_account_is_refused() =>
        Assert.False((await Build().Service.ResetPasswordAsync(OwnerId, Temp, OwnerId)).Succeeded);

    [Fact]
    public async Task Changing_own_password_clears_the_flag_and_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        repo.Users[0].MustChangePassword = true;
        var before = repo.Users[0].SecurityStamp;
        var result = await s.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "a-brand-new-pass");
        Assert.True(result.Succeeded, result.Error);
        Assert.False(repo.Users[0].MustChangePassword);
        Assert.Equal("hash:a-brand-new-pass", repo.Users[0].PasswordHash);
        Assert.NotEqual(before, repo.Users[0].SecurityStamp);
    }

    [Fact]
    public async Task Changing_own_password_needs_the_current_one() =>
        Assert.Equal("Current password is incorrect.",
            (await Build().Service.ChangeOwnPasswordAsync(OwnerId, "wrong", "a-brand-new-pass")).Error);

    [Fact]
    public async Task New_password_must_differ_from_the_current_one() =>
        Assert.False((await Build().Service.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "owner-password-1")).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task New_password_cannot_be_blank(string password) =>
        Assert.Equal("Enter a password.", (await Build().Service.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", password)).Error);

    [Fact]
    public async Task New_password_cannot_exceed_128_characters() =>
        Assert.Equal("Password must be 128 characters or fewer.",
            (await Build().Service.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", new string('p', 129))).Error);

    [Fact]
    public async Task New_password_of_one_character_is_accepted()
    {
        var (s, repo, _) = Build();
        var result = await s.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "p");
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("hash:p", repo.Users[0].PasswordHash);
    }

    private sealed class FakeEmails : IOnboardingStore
    {
        public bool Taken { get; set; }
        public string? Checked { get; private set; }
        public Task<bool> EmailExistsAsync(string email, CancellationToken ct) { Checked = email; return Task.FromResult(Taken); }
        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
        public Task<(int tenantId, int ownerUserId)> CreateTenantGraphAsync(Tenant tenant, TenantSettings settings,
            PipelineTemplate template, string ownerName, string ownerEmail, string ownerPasswordHash, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHasher : IIdentityService
    {
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string hash, string password) => hash == "hash:" + password;
        public Task<SignInResult> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserSession?> GetSessionAsync(int userId, int tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
