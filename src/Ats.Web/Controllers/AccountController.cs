using Ats.Application.Abstractions;
using Ats.Application.Tenancy;
using Ats.Web.Identity;
using Ats.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

[AllowAnonymous]
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

        var signIn = await _identity.ValidateCredentialsAsync(vm.OwnerEmail, vm.Password);
        if (!signIn.Succeeded) return RedirectToAction("Login");

        await AtsSignIn.SignInAsync(HttpContext, signIn.UserId!.Value, signIn.TenantId!.Value, signIn.Role!,
            signIn.DisplayName ?? "", signIn.SecurityStamp!.Value, signIn.MustChangePassword);
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

        await AtsSignIn.SignInAsync(HttpContext, result.UserId!.Value, result.TenantId!.Value, result.Role!,
            result.DisplayName ?? "", result.SecurityStamp!.Value, result.MustChangePassword);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AtsSignIn.Scheme);
        return RedirectToAction("Login");
    }
}
