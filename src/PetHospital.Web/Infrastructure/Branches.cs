using System.Security.Claims;
using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Xác định chi nhánh đang làm việc của người dùng:
/// - claim "br": danh sách chi nhánh được làm ("*" = mọi chi nhánh), claim "brd": chi nhánh mặc định (ghi lúc đăng nhập);
/// - cookie "pet.branch": chi nhánh đang chọn (chỉ có hiệu lực khi nằm trong danh sách được làm).
/// </summary>
public static class BranchScope
{
    public const string AllowedClaim = "br";
    public const string DefaultClaim = "brd";
    public const string Cookie = "pet.branch";
    public const string All = "*";

    /// <summary>Chi nhánh đang làm việc; null khi chưa đăng nhập (khởi tạo dữ liệu, tác vụ nền)</summary>
    public static int? Resolve(HttpContext? ctx)
    {
        var user = ctx?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        var allowed = user.FindFirstValue(AllowedClaim);
        if (allowed == null) return -1; // phiên đăng nhập cũ: không thấy dữ liệu (sẽ bị yêu cầu đăng nhập lại)
        if (int.TryParse(ctx!.Request.Cookies[Cookie], out var selected) && selected > 0 && IsAllowed(allowed, selected)) return selected;
        return int.TryParse(user.FindFirstValue(DefaultClaim), out var d) && d > 0 ? d : -1;
    }

    public static bool IsAllowed(string? allowedClaim, int branchId) =>
        allowedClaim == All || (allowedClaim ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(branchId.ToString());

    /// <summary>Danh sách chi nhánh được làm; null = mọi chi nhánh</summary>
    public static List<int>? AllowedIds(ClaimsPrincipal user)
    {
        var allowed = user.FindFirstValue(AllowedClaim);
        if (allowed == All) return null;
        return (allowed ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();
    }

    /// <summary>Claim chi nhánh khi đăng nhập. Trả về null nếu người dùng chưa được gán chi nhánh nào đang hoạt động.</summary>
    public static async Task<List<Claim>?> ClaimsForAsync(PetDbContext db, AppUser user)
    {
        var active = await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).Select(b => b.Id).ToListAsync();
        var all = user.AllBranches || user.RoleList().Contains(AppRoles.Admin);
        var mine = all ? active
            : (await db.UserBranches.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.BranchId).ToListAsync()).Where(active.Contains)
                .OrderBy(active.IndexOf).ToList();
        if (mine.Count == 0) return null;
        return new List<Claim>
        {
            new(AllowedClaim, all ? All : string.Join(",", mine)),
            new(DefaultClaim, mine[0].ToString())
        };
    }
}

/// <summary>Tham số dùng chung toàn chuỗi đọc rất thường xuyên (nạp khi khởi động, cập nhật khi lưu cài đặt)</summary>
public static class ChainSettings
{
    /// <summary>Khách hàng & hồ sơ thú cưng dùng chung giữa các chi nhánh</summary>
    public static volatile bool ShareCustomers = true;
}
