using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Core.Security;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Services;

namespace Tracer.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    AdminAuditService adminAuditService) : PageModel
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string ReturnUrl { get; set; } = "/";

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        ReturnUrl = NormalizeReturnUrl(returnUrl);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = NormalizeReturnUrl(ReturnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();

        var normalizedUserName = Input.UserName.Trim().ToUpperInvariant();
        var admin = await dbContext.AdminUsers
            .SingleOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);

        if (admin is null)
        {
            await adminAuditService.WriteLoginAttemptAsync(Input.UserName.Trim(), ipAddress, userAgent, false, "Unknown username.", null, cancellationToken);
            ModelState.AddModelError(string.Empty, "Invalid admin credentials.");
            return Page();
        }

        if (!admin.IsActive)
        {
            await adminAuditService.WriteLoginAttemptAsync(admin.UserName, ipAddress, userAgent, false, "Account is disabled.", admin.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "Invalid admin credentials.");
            return Page();
        }

        if (admin.LockedUntilUtc.HasValue && admin.LockedUntilUtc > DateTimeOffset.UtcNow)
        {
            await adminAuditService.WriteLoginAttemptAsync(admin.UserName, ipAddress, userAgent, false, "Account is temporarily locked.", admin.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, $"Account locked until {admin.LockedUntilUtc.Value.LocalDateTime:g}.");
            return Page();
        }

        if (!AdminPasswordHasher.VerifyHashedPassword(admin.PasswordHash, Input.Password))
        {
            admin.FailedLoginCount += 1;
            if (admin.FailedLoginCount >= MaxFailedAttempts)
            {
                admin.LockedUntilUtc = DateTimeOffset.UtcNow.Add(LockoutDuration);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await adminAuditService.WriteLoginAttemptAsync(admin.UserName, ipAddress, userAgent, false, "Invalid password.", admin.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "Invalid admin credentials.");
            return Page();
        }

        admin.FailedLoginCount = 0;
        admin.LockedUntilUtc = null;
        admin.LastLoginUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await adminAuditService.WriteLoginAttemptAsync(admin.UserName, ipAddress, userAgent, true, null, admin.Id, cancellationToken);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.UserName),
            new Claim(ClaimTypes.Role, admin.Role.ToString()),
            new Claim("tracer:role", admin.Role.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = Input.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(Input.RememberMe ? 24 * 14 : 8)
            });

        return LocalRedirect(ReturnUrl);
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }
    }
}
