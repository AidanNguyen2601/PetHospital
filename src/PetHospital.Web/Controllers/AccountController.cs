using System.Security.Claims;
using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using PetHospital.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Controllers;

public class AccountController : AppController
{
    private readonly IPasswordHasher<AppUser> _hasher;
    public AccountController(PetDbContext db, IPasswordHasher<AppUser> hasher) : base(db) => _hasher = hasher;

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Username == vm.Username);
        if (user == null || !user.IsActive ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, vm.Password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng, hoặc tài khoản đã bị khóa.");
            return View(vm);
        }
        var branchClaims = await BranchScope.ClaimsForAsync(Db, user);
        if (branchClaims == null)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản chưa được gán chi nhánh đang hoạt động. Liên hệ quản trị hệ thống.");
            return View(vm);
        }
        await SignInAsync(user, vm.RememberMe, branchClaims);
        // Bắt đầu phiên ở chi nhánh mặc định
        Response.Cookies.Delete(BranchScope.Cookie);
        // Ngôn ngữ: trình duyệt đã chọn (trên trang đăng nhập / lần trước) thì lưu vào hồ sơ;
        // trình duyệt chưa chọn thì dùng ngôn ngữ đã lưu trong hồ sơ người dùng
        if (Request.Cookies.ContainsKey(LangScope.Cookie) && LangScope.Current != null) user.Language = LangScope.Current.Code;
        else if (Svc<Localizer>().Find(user.Language) is { IsActive: true } lang)
            Response.Cookies.Append(LangScope.Cookie, lang.Code,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax, HttpOnly = true });
        user.LastLoginAt = DateTime.Now;
        await Db.SaveChangesAsync();
        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    private async Task SignInAsync(AppUser user, bool persistent, List<Claim> branchClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimsExtensions.FullNameClaim, user.DisplayName),
            new(ClaimsExtensions.VetClaim, user.IsVet ? "1" : "0")
        };
        claims.AddRange(branchClaims);
        foreach (var r in user.RoleList()) claims.Add(new Claim(ClaimTypes.Role, r));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = persistent });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await Db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();
        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, vm.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "Mật khẩu hiện tại không đúng");
            return View(vm);
        }
        user.PasswordHash = _hasher.HashPassword(user, vm.NewPassword);
        await Db.SaveChangesAsync();
        return BackWithSuccess("Đổi mật khẩu thành công.", "Index", new { controller = "Home" });
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
