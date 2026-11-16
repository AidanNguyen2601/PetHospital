using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Data;

/// <summary>Tạo database (nếu chưa có), danh mục nền và dữ liệu mẫu để chạy thử.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, bool seedSampleData = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();

        await db.Database.EnsureCreatedAsync();
        var log = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("PetHospital.SchemaUpgrade");
        db.DefaultBranchId = await SchemaUpgrade.RunAsync(db, log);
        await EnsureBaseDataAsync(db);
        ChainSettings.ShareCustomers = (await db.SystemSettings.Where(s => s.Key == SettingsService.ShareCustomers).Select(s => s.Value).FirstOrDefaultAsync() ?? "1") != "0";

        if (!await db.Users.AnyAsync())
        {
            if (seedSampleData) await SampleData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<StockService>(), hasher);
            else
            {
                var admin = new AppUser { Username = "admin", FullName = "Quản trị hệ thống", Roles = AppRoles.Admin, AllBranches = true };
                admin.PasswordHash = hasher.HashPassword(admin, "Admin@123");
                db.Users.Add(admin);
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>Danh mục nền – chỉ thêm phần còn thiếu, chạy lại an toàn</summary>
    public static async Task EnsureBaseDataAsync(PetDbContext db)
    {
        var configured = await db.RolePermissions.Select(r => r.Role).Distinct().ToListAsync();
        foreach (var (role, perms) in Perm.Defaults)
        {
            if (configured.Contains(role)) continue;
            foreach (var p in perms) db.RolePermissions.Add(new RolePermission { Role = role, Permission = p });
        }

        if (!await db.Species.AnyAsync())
        {
            var data = new (string Name, string[] Breeds)[]
            {
                ("Chó", new[] { "Chó ta (Việt Nam)", "Poodle", "Phốc sóc (Pomeranian)", "Corgi", "Golden Retriever", "Labrador", "Husky", "Alaska", "Becgie (German Shepherd)", "Chihuahua", "Shiba Inu", "Pug", "Bull Pháp", "Lai" }),
                ("Mèo", new[] { "Mèo ta", "Anh lông ngắn (British Shorthair)", "Anh lông dài", "Ba Tư (Persian)", "Scottish Fold", "Xiêm (Siamese)", "Bengal", "Munchkin", "Lai" }),
                ("Thỏ", new[] { "Thỏ Hà Lan", "Thỏ tai cụp (Holland Lop)", "Thỏ sư tử" }),
                ("Chim", new[] { "Vẹt", "Chào mào", "Chim cảnh khác" }),
                ("Bò sát", new[] { "Rùa", "Rồng Nam Mỹ (Iguana)", "Rắn cảnh" }),
                ("Loài khác", new[] { "Hamster", "Sóc", "Nhím kiểng" })
            };
            var i = 0;
            foreach (var (name, breeds) in data)
            {
                var s = new Species { Name = name, SortOrder = ++i };
                foreach (var b in breeds) s.Breeds.Add(new Breed { Name = b });
                db.Species.Add(s);
            }
        }
        await db.SaveChangesAsync();
    }
}
