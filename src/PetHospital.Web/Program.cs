using System.Globalization;
using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

// Nhập số theo chuẩn bất biến (ô nhập số HTML luôn dùng dấu chấm thập phân);
// hiển thị tiền tệ, ngày tháng kiểu Việt Nam qua các hàm ToMoney / ToDate.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("vi-VN");

var builder = WebApplication.CreateBuilder(args);

// ---------- Database (SQL Server 2022) ----------
builder.Services.AddDbContext<PetDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PetConnection"),
        sql => sql.CommandTimeout(120)));
builder.Services.AddHttpContextAccessor();

// ---------- Xác thực cookie ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Cookie.Name = "pet.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // Phiên đăng nhập trước khi có chi nhánh: yêu cầu đăng nhập lại để nhận quyền chi nhánh
        o.Events.OnValidatePrincipal = async ctx =>
        {
            if (ctx.Principal?.FindFirst(BranchScope.AllowedClaim) == null)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddControllersWithViews(o =>
{
    // Mọi trang yêu cầu đăng nhập (trừ [AllowAnonymous])
    o.Conventions.Add(new RequireLoginConvention());
    // Chỉ các trường gắn [Required] mới bắt buộc
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(f => L.T("{0} phải là số.", f));
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, f) => L.T("Giá trị \"{0}\" không hợp lệ cho {1}.", v, f));
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(f => L.T("{0} không được để trống.", f));
})
// Tên trường (Display) và thông báo kiểm tra dữ liệu dịch theo ngôn ngữ giao diện
.AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(Program)));

// ---------- Đa ngôn ngữ (bản dịch lưu trong database, sửa tại Quản trị › Ngôn ngữ) ----------
builder.Services.AddSingleton<Localizer>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, DbStringLocalizerFactory>();
builder.Services.AddHostedService<TranslationKeyCollector>();

// Lưu khóa mã hóa cookie ra volume để khởi động lại container không bị đăng xuất
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("PetHospital");
}

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- Dịch vụ nghiệp vụ ----------
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<SecurityCache>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<Lookups>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<InvoiceService>();

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
if (!string.IsNullOrEmpty(app.Configuration["ASPNETCORE_HTTPS_PORTS"]) || !string.IsNullOrEmpty(app.Configuration["HTTPS_PORTS"]))
    app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.UseStaticFiles();
app.UseMiddleware<LanguageMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// Tạo database + danh mục nền; dữ liệu mẫu chỉ nạp khi DB còn trống và Database:SeedOnStartup = true.
// Thử lại nhiều lần vì khi chạy bằng Docker, SQL Server có thể khởi động chậm hơn web app.
{
    var seedSample = app.Configuration.GetValue("Database:SeedOnStartup", true);
    var retries = app.Configuration.GetValue("Database:StartupRetries", 12);
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await DbSeeder.SeedAsync(app.Services, seedSample);
            // Ngôn ngữ cài sẵn + bản dịch i18n/*.json (không ghi đè câu đã sửa), rồi nạp vào bộ nhớ
            using (var scope = app.Services.CreateScope())
                await Localizer.SeedAsync(scope.ServiceProvider.GetRequiredService<PetDbContext>(), app.Environment.ContentRootPath, app.Logger);
            await app.Services.GetRequiredService<Localizer>().ReloadAsync();
            break;
        }
        catch (Exception ex) when (attempt < retries)
        {
            app.Logger.LogWarning("Chưa kết nối được database (lần {Attempt}/{Max}): {Message}. Thử lại sau 5 giây...",
                attempt, retries, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Không khởi tạo được database. Kiểm tra ConnectionStrings:PetConnection");
            throw;
        }
    }
}

// Lệnh mô phỏng luồng dữ liệu rồi thoát (không mở web):
//   dotnet PetHospital.Web.dll --simulate [số ngày=30] [mức khách 1|2|3 = 2]
var simArg = Array.IndexOf(args, "--simulate");
if (simArg >= 0)
{
    var simDays = args.Length > simArg + 1 && int.TryParse(args[simArg + 1], out var dd) ? dd : 30;
    var simLevel = args.Length > simArg + 2 && int.TryParse(args[simArg + 2], out var ll) ? ll : 2;
    // Chi nhánh mô phỏng: --branch <mã chi nhánh> (mặc định chi nhánh đầu tiên)
    var brArg = Array.IndexOf(args, "--branch");
    using var scope = app.Services.CreateScope();
    var simDb = scope.ServiceProvider.GetRequiredService<PetDbContext>();
    var branchCode = brArg >= 0 && args.Length > brArg + 1 ? args[brArg + 1] : null;
    var simBranch = await simDb.Branches.Where(b => branchCode == null || b.Code == branchCode).OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
        .Select(b => (int?)b.Id).FirstOrDefaultAsync() ?? throw new InvalidOperationException($"Không có chi nhánh {branchCode}");
    simDb.CurrentBranchId = simBranch;
    var r = await FlowSimulator.RunAsync(simDb, scope.ServiceProvider.GetRequiredService<StockService>(), simDays, simLevel);
    Console.WriteLine($"[PET] Mô phỏng {r.Days} ngày: {r.NewOwners} KH mới, {r.Appointments} lịch hẹn, {r.Visits} phiếu khám, {r.Vaccinations} mũi tiêm, " +
                      $"{r.Admissions} nội trú, {r.Receipts} phiếu nhập, {r.Invoices} hóa đơn, {r.Payments} lần thu, doanh thu {r.Revenue:N0}");
    return;
}

app.Run();

/// <summary>Gắn yêu cầu đăng nhập (cookie) cho mọi controller</summary>
public class RequireLoginConvention : IControllerModelConvention
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().Build();

    public void Apply(ControllerModel controller) => controller.Filters.Add(new AuthorizeFilter(Policy));
}
