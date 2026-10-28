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
    private const int OwnerId = 1;

    private static (UserService Service, FakeUserRepository Repo, FakeOnboardingStore Emails) Build() => Build(new FakeEmailOutbox());

    private static (UserService Service, FakeUserRepository Repo, FakeOnboardingStore Emails) Build(FakeEmailOutbox outbox)
    {
        var repo = new FakeUserRepository();
        repo.Users.Add(new AppUser { Id = OwnerId, Email = "owner@acme.test", DisplayName = "Owner", Role = AtsRole.Owner, PasswordHash = "hash:owner-password-1" });
        repo.Users.Add(new AppUser { Id = 2, Email = "rec@acme.test", DisplayName = "Rec", Role = AtsRole.Recruiter, PasswordHash = "hash:x" });
        var emails = new FakeOnboardingStore();
        return (new UserService(repo, emails, new FakeIdentityService(), outbox, new FakeTenantBranding()), repo, emails);
    }

    private static CreateUserInput Input(string role = AtsRole.Viewer, string email = " New.User@Acme.test ") =>
        new("New User", email, role);

    // Decision D1 (28 October 2026): new users are invited by email and choose their own password.
    [Fact]
    public async Task Create_adds_an_active_invite_pending_user_without_a_password()
    {
        var (s, repo, emails) = Build();
        var (result, userId) = await s.CreateAsync(Input());
        Assert.True(result.Succeeded, result.Error);
        var u = repo.Users.Single(x => x.Email == "new.user@acme.test");
        Assert.Equal(u.Id, userId);
        Assert.True(u.IsActive);
        Assert.False(u.MustChangePassword);
        Assert.Equal("", u.PasswordHash);
        Assert.True(u.IsInvitePending);
        Assert.Equal(AtsRole.Viewer, u.Role);
        Assert.Equal("new.user@acme.test", emails.Checked);
    }

    [Fact]
    public async Task Create_stages_no_email_itself()
    {
        var outbox = new FakeEmailOutbox();
        Assert.True((await Build(outbox).Service.CreateAsync(Input())).Result.Succeeded);
        Assert.Empty(outbox.Staged);
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
        Assert.False((await Build().Service.CreateAsync(new CreateUserInput(new string('a', 201), "a@acme.test", AtsRole.Viewer))).Result.Succeeded);

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Create_rejects_invalid_email(string email) =>
        Assert.False((await Build().Service.CreateAsync(Input(email: email))).Result.Succeeded);

    [Fact]
    public async Task Create_rejects_a_blank_name() =>
        Assert.False((await Build().Service.CreateAsync(new CreateUserInput("  ", "a@acme.test", AtsRole.Viewer))).Result.Succeeded);

    // An invited Owner has never signed in, so they cannot stand in for the last active Owner.
    [Fact]
    public async Task An_invite_pending_Owner_does_not_count_as_another_active_Owner()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 5, Email = "invited@acme.test", DisplayName = "Invited", Role = AtsRole.Owner, PasswordHash = "" });
        repo.Users.Add(new AppUser { Id = 6, Email = "owner2@acme.test", DisplayName = "Owner 2", Role = AtsRole.Owner, PasswordHash = "hash:y" });
        Assert.True((await s.SetActiveAsync(6, false, 5)).Result.Succeeded);
        Assert.Equal("A workspace needs at least one active Owner.", (await s.SetActiveAsync(OwnerId, false, 5)).Result.Error);
    }

    [Fact]
    public async Task An_invite_pending_Owner_can_be_deactivated_or_demoted_while_one_real_Owner_remains()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 5, Email = "invited@acme.test", DisplayName = "Invited", Role = AtsRole.Owner, PasswordHash = "" });
        Assert.True((await s.UpdateAsync(new UpdateUserInput(5, "Invited", "invited@acme.test", AtsRole.Viewer), OwnerId)).Result.Succeeded);
        repo.Users[2].Role = AtsRole.Owner;
        Assert.True((await s.SetActiveAsync(5, false, OwnerId)).Result.Succeeded);
    }

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

    // An accepted user's address changes only when they confirm it from the new mailbox (IEmailChangeService).
    [Fact]
    public async Task Update_changing_an_accepted_users_email_checks_it_and_returns_it_pending_without_saving()
    {
        var (s, repo, emails) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(email: " New.Rec@Acme.test "), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("new.rec@acme.test", update.PendingEmail);
        Assert.Equal("new.rec@acme.test", emails.Checked);
        Assert.Equal("rec@acme.test", repo.Users[1].Email);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
        Assert.Empty(update.ChangedFields);
        Assert.False(update.SignedOut);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Update_changing_name_and_email_saves_the_name_and_leaves_the_email_pending()
    {
        var (s, repo, _) = Build();
        var before = repo.Users[1].SecurityStamp;
        var update = await s.UpdateAsync(Edit(name: "Rec Renamed", email: "new.rec@acme.test"), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("Rec Renamed", repo.Users[1].DisplayName);
        Assert.Equal("rec@acme.test", repo.Users[1].Email);
        Assert.Equal("new.rec@acme.test", update.PendingEmail);
        Assert.Equal(new[] { UserField.Name }, update.ChangedFields);
        Assert.Equal(before, repo.Users[1].SecurityStamp);
        Assert.Equal(1, repo.SaveCount);
    }

    // The old invite link names the old address, so the stamp rotates; the caller sends a new invite.
    [Fact]
    public async Task Update_changing_an_invite_pending_users_email_applies_it_at_once_and_rotates_the_stamp()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 5, Email = "invited@acme.test", DisplayName = "Invited", Role = AtsRole.Viewer, PasswordHash = "" });
        var before = repo.Users[2].SecurityStamp;
        var update = await s.UpdateAsync(Edit(5, "Invited", " Fixed@Acme.test", AtsRole.Viewer), OwnerId);
        Assert.True(update.Result.Succeeded, update.Result.Error);
        Assert.Equal("fixed@acme.test", repo.Users[2].Email);
        Assert.Null(update.PendingEmail);
        Assert.Equal(new[] { UserField.Email }, update.ChangedFields);
        Assert.NotEqual(before, repo.Users[2].SecurityStamp);
        Assert.Equal(1, repo.SaveCount);
    }

    // A deactivated user could not confirm the link, so the change is refused up front.
    [Fact]
    public async Task Update_refuses_an_email_change_for_a_deactivated_accepted_user()
    {
        var (s, repo, _) = Build();
        repo.Users[1].IsActive = false;
        var update = await s.UpdateAsync(Edit(name: "Rec Renamed", email: "new.rec@acme.test"), OwnerId);
        Assert.Equal("Reactivate the user before changing their email address.", update.Result.Error);
        Assert.Null(update.PendingEmail);
        Assert.Equal("Rec", repo.Users[1].DisplayName);
        Assert.Equal(0, repo.SaveCount);
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
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 5, Email = "invited@acme.test", DisplayName = "Invited", Role = AtsRole.Viewer, PasswordHash = "" });
        var update = await s.UpdateAsync(new UpdateUserInput(5, "Other", "other@acme.test", AtsRole.Recruiter), OwnerId);
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
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", PasswordHash = "hash:o2" });
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
        repo.Users.Add(new AppUser { Id = 5, Email = "invited@acme.test", DisplayName = "Invited", Role = AtsRole.Viewer, PasswordHash = "" });
        repo.RejectNextSaveAsDuplicate = true;
        var update = await s.UpdateAsync(Edit(5, "Invited", "taken@acme.test", AtsRole.Viewer), OwnerId);
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
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", PasswordHash = "hash:o2", IsActive = false });
        var update = await s.UpdateAsync(Edit(OwnerId, "Owner", "owner@acme.test", AtsRole.Viewer), actingUserId: 3);
        Assert.Equal("A workspace needs at least one active Owner.", update.Result.Error);
        Assert.Equal(AtsRole.Owner, repo.Users[0].Role);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task An_Owner_can_be_demoted_when_another_active_Owner_exists()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", PasswordHash = "hash:o2" });
        Assert.True((await s.UpdateAsync(Edit(3, "O2", "o2@acme.test", AtsRole.Recruiter), OwnerId)).Result.Succeeded);
        Assert.Equal(AtsRole.Recruiter, repo.Users[2].Role);
    }

    [Fact]
    public async Task An_inactive_Owner_can_be_demoted_while_another_active_Owner_exists()
    {
        var (s, repo, _) = Build();
        repo.Users.Add(new AppUser { Id = 3, Role = AtsRole.Owner, Email = "o2@acme.test", DisplayName = "O2", PasswordHash = "hash:o2", IsActive = false });
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
    public async Task Deactivating_tells_the_user_in_the_same_save()
    {
        var outbox = new FakeEmailOutbox();
        var (s, repo, _) = Build(outbox);
        Assert.True((await s.SetActiveAsync(2, false, OwnerId)).Changed);
        var (draft, _) = Assert.Single(outbox.Staged);
        Assert.Equal("rec@acme.test", draft.To);
        Assert.Equal("Your account for Acme was deactivated", draft.Subject);
        Assert.Equal(0, outbox.SendCount);
        Assert.Equal(1, repo.SaveCount);
    }

    [Fact]
    public async Task Reactivating_sends_no_email()
    {
        var outbox = new FakeEmailOutbox();
        var (s, repo, _) = Build(outbox);
        repo.Users[1].IsActive = false;
        Assert.True((await s.SetActiveAsync(2, true, OwnerId)).Changed);
        Assert.Empty(outbox.Staged);
    }

    // An invite-pending user never had access, so there is nothing to tell them.
    [Fact]
    public async Task Deactivating_an_invite_pending_user_sends_no_email()
    {
        var outbox = new FakeEmailOutbox();
        var (s, repo, _) = Build(outbox);
        repo.Users[1].PasswordHash = "";
        Assert.True((await s.SetActiveAsync(2, false, OwnerId)).Changed);
        Assert.False(repo.Users[1].IsActive);
        Assert.Empty(outbox.Staged);
    }

    [Fact]
    public async Task A_refused_or_repeated_deactivation_sends_no_email()
    {
        var outbox = new FakeEmailOutbox();
        var (s, repo, _) = Build(outbox);
        repo.Users[1].IsActive = false;
        Assert.False((await s.SetActiveAsync(2, false, OwnerId)).Changed);
        Assert.False((await s.SetActiveAsync(OwnerId, false, actingUserId: 3)).Changed);
        Assert.Empty(outbox.Staged);
    }

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
    public async Task Changing_own_password_stages_a_notice_to_the_user()
    {
        var outbox = new FakeEmailOutbox();
        var (s, repo, _) = Build(outbox);
        repo.Users[0].TenantId = 7;
        Assert.True((await s.ChangeOwnPasswordAsync(OwnerId, "owner-password-1", "a-brand-new-pass")).Succeeded);
        var (draft, tenantId) = Assert.Single(outbox.Staged);
        Assert.Equal("owner@acme.test", draft.To);
        Assert.Equal("Your password for Acme was changed", draft.Subject);
        Assert.Equal(7, tenantId);
        Assert.DoesNotContain("a-brand-new-pass", draft.TextBody);
    }

    [Fact]
    public async Task A_refused_password_change_stages_nothing()
    {
        var outbox = new FakeEmailOutbox();
        Assert.False((await Build(outbox).Service.ChangeOwnPasswordAsync(OwnerId, "wrong", "a-brand-new-pass")).Succeeded);
        Assert.Empty(outbox.Staged);
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
}
