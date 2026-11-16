using System.Security.Claims;
using PetHospital.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace PetHospital.Web.Infrastructure;

/// <summary>Bộ nhớ đệm ma trận vai trò – quyền (nạp lại khi quản trị thay đổi)</summary>
public class SecurityCache
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private const string PermKey = "sec.perms";

    public SecurityCache(IServiceScopeFactory scopes, IMemoryCache cache) { _scopes = scopes; _cache = cache; }

    public void Invalidate() => _cache.Remove(PermKey);

    private Dictionary<string, HashSet<string>> RolePerms() => _cache.GetOrCreate(PermKey, e =>
    {
        e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetDbContext>();
        return db.RolePermissions.AsNoTracking().ToList()
            .GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Select(x => x.Permission).ToHashSet());
    })!;

    public bool Has(IEnumerable<string> roles, string perm)
    {
        var map = RolePerms();
        return roles.Any(r => r == AppRoles.Admin || (map.TryGetValue(r, out var set) && set.Contains(perm)));
    }
}

/// <summary>Thông tin người dùng hiện tại: vai trò, quyền chức năng</summary>
public class CurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly SecurityCache _sec;

    public CurrentUser(IHttpContextAccessor http, SecurityCache sec) { _http = http; _sec = sec; }

    public ClaimsPrincipal Principal => _http.HttpContext?.User ?? new ClaimsPrincipal();
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public int Id => Principal.GetUserId();
    public string FullName => Principal.GetFullName();
    public IEnumerable<string> Roles => Principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
    public bool InRole(string role) => Principal.IsInRole(role);
    public bool IsAdmin => InRole(AppRoles.Admin);
    public bool IsVet => Principal.FindFirstValue(ClaimsExtensions.VetClaim) == "1";

    public bool Has(string perm) => IsAuthenticated && _sec.Has(Roles, perm);
    public bool HasAny(params string[] perms) => perms.Any(Has);

    /// <summary>Được xem giá vốn / lãi gộp</summary>
    public bool CanSeeCost => Has(Perm.StockCost);

    // ----- Chi nhánh -----

    /// <summary>Chi nhánh đang làm việc</summary>
    public int BranchId => BranchScope.Resolve(_http.HttpContext) ?? 0;

    /// <summary>Làm việc ở mọi chi nhánh (quản trị / quản lý chuỗi)</summary>
    public bool AllBranches => Principal.FindFirstValue(BranchScope.AllowedClaim) == BranchScope.All;

    /// <summary>Chi nhánh được làm việc; null = mọi chi nhánh</summary>
    public List<int>? BranchIds => BranchScope.AllowedIds(Principal);

    public bool CanAccessBranch(int branchId) => BranchScope.IsAllowed(Principal.FindFirstValue(BranchScope.AllowedClaim), branchId);
}

/// <summary>Yêu cầu quyền chức năng: [RequirePerm(Perm.X)] (nhiều quyền = chỉ cần một)</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _perms;
    public RequirePermAttribute(params string[] perms) => _perms = perms;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) { context.Result = new ChallengeResult(); return; }
        var me = context.HttpContext.RequestServices.GetRequiredService<CurrentUser>();
        if (!_perms.Any(me.Has)) context.Result = new ForbidResult();
    }
}
