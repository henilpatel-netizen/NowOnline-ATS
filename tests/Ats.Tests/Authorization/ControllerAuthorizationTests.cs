using System.Reflection;
using Ats.Domain.Authorization;
using Ats.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ats.Tests.Authorization;

// Guards the controller surface: every action is either deliberately public or gated by a named
// permission policy. A bare [Authorize] or [Authorize(Roles = ...)] fails, so role checks cannot creep back.
public class ControllerAuthorizationTests
{
    private static readonly Type[] Controllers = typeof(HomeController).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract).ToArray();

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    private static List<object> Attributes(MethodInfo action) =>
        action.DeclaringType!.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true)).ToList();

    [Fact]
    public void Every_action_is_anonymous_or_permission_gated()
    {
        var offenders = new List<string>();
        foreach (var c in Controllers)
            foreach (var a in Actions(c))
            {
                var attrs = Attributes(a);
                if (attrs.OfType<AllowAnonymousAttribute>().Any()) continue;
                var authorize = attrs.OfType<AuthorizeAttribute>().ToList();
                if (authorize.Count == 0 || authorize.Any(x => x.Policy is null || !AtsPermission.All.Contains(x.Policy)))
                    offenders.Add($"{c.Name}.{a.Name}");
            }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Only_the_expected_controllers_are_anonymous()
    {
        var anonymous = Controllers.Where(c => c.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(c => c.FullName).Order();
        Assert.Equal(new[]
        {
            "Ats.Web.Areas.Careers.Controllers.JobsController",
            "Ats.Web.Controllers.AccountController",
            "Ats.Web.Controllers.HomeController",
        }, anonymous);
    }

    // Action-level [AllowAnonymous] beats every class-level policy, so it would pass the test above unseen.
    [Fact]
    public void No_back_office_action_is_anonymous()
    {
        var offenders = Controllers
            .Where(c => c.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .SelectMany(c => Actions(c).Where(a => a.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(a => $"{c.Name}.{a.Name}"));
        Assert.Empty(offenders);
    }

    // Spot-checks that the matrix reached the actions that matter most.
    [Theory]
    [InlineData(typeof(JobsController), "Delete", true, AtsPermission.JobsManage)]
    [InlineData(typeof(JobsController), "Edit", true, AtsPermission.JobsManage)]
    [InlineData(typeof(JobsController), "Edit", false, AtsPermission.JobsView)]
    [InlineData(typeof(CandidatesController), "Delete", true, AtsPermission.CandidatesManage)]
    [InlineData(typeof(CandidatesController), "Edit", false, AtsPermission.CandidatesView)]
    [InlineData(typeof(BoardController), "Move", true, AtsPermission.ApplicationsMove)]
    [InlineData(typeof(ResumeController), "Download", false, AtsPermission.ResumesDownload)]
    [InlineData(typeof(IntegrationController), "Index", true, AtsPermission.IntegrationManage)]
    [InlineData(typeof(AuditController), "Index", false, AtsPermission.AuditView)]
    [InlineData(typeof(CareerSiteController), "Branding", true, AtsPermission.CareerSiteManage)]
    [InlineData(typeof(PipelinesController), "Save", true, AtsPermission.PipelinesManage)]
    [InlineData(typeof(DepartmentsController), "Delete", true, AtsPermission.OrganisationManage)]
    public void Action_requires_policy(Type controller, string action, bool post, string policy)
    {
        var method = Actions(controller).Single(m => m.Name == action &&
            (m.GetCustomAttribute<HttpPostAttribute>() is not null) == post);
        Assert.Contains(policy, Attributes(method).OfType<AuthorizeAttribute>().Select(a => a.Policy));
    }

    [Fact]
    public void Edit_get_is_not_gated_by_manage()
    {
        foreach (var (c, manage) in new[] { (typeof(JobsController), AtsPermission.JobsManage),
                     (typeof(CandidatesController), AtsPermission.CandidatesManage) })
        {
            var get = Actions(c).Single(m => m.Name == "Edit" && m.GetCustomAttribute<HttpPostAttribute>() is null);
            Assert.DoesNotContain(manage, Attributes(get).OfType<AuthorizeAttribute>().Select(a => a.Policy));
        }
    }
}
