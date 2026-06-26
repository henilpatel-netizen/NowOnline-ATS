using System.Security.Claims;
using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

public class AccountController : Controller
{
    private readonly ITenantOnboardingService _onboarding;
    private readonly IIdentityService _identity;

    public AccountController(ITenantOnboardingService onboarding, IIdentityService identity)
    {
        _onboarding = onboarding;
        _identity = identity;
    }

    [HttpGet] public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _onboarding.RegisterAsync(
            new RegisterTenantInput(vm.CompanyName, vm.Slug, vm.OwnerName, vm.OwnerEmail, vm.Password));

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
            return View(vm);
        }

        await SignInAsync(result.OwnerUserId, result.TenantId, "Owner");
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet] public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await _identity.ValidateCredentialsAsync(vm.Email, vm.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Invalid credentials.");
            return View(vm);
        }

        await SignInAsync(result.UserId!.Value, result.TenantId!.Value, result.Role!);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AtsCookie");
        return RedirectToAction("Login");
    }

    private async Task SignInAsync(int userId, int tenantId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "AtsCookie");
        await HttpContext.SignInAsync("AtsCookie", new ClaimsPrincipal(identity));
    }
}
