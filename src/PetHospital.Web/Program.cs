using System.Globalization;
using PetHospital.Web.Data;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
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
    });

builder.Services.AddControllersWithViews(o =>
{
    // Mọi trang yêu cầu đăng nhập (trừ [AllowAnonymous])
    o.Conventions.Add(new RequireLoginConvention());
    // Chỉ các trường gắn [Required] mới bắt buộc
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(f => $"{f} phải là số.");
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, f) => $"Giá trị \"{v}\" không hợp lệ cho {f}.");
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(f => $"{f} không được để trống.");
});

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
    using var scope = app.Services.CreateScope();
    var r = await FlowSimulator.RunAsync(scope.ServiceProvider.GetRequiredService<PetDbContext>(),
        scope.ServiceProvider.GetRequiredService<StockService>(), simDays, simLevel);
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
