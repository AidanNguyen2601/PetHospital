using System.Text.RegularExpressions;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Data;

/// <summary>
/// Nâng cấp database đang chạy (tạo bằng EnsureCreated, không dùng migration) lên mô hình chuỗi nhiều chi nhánh.
/// Chạy lại an toàn: chỉ tạo bảng / cột còn thiếu. Dữ liệu cũ được gán cho chi nhánh mặc định (chi nhánh đầu tiên).
/// </summary>
public static class SchemaUpgrade
{
    /// <summary>Bảng mới của mô hình chi nhánh (tạo theo đúng script EF sinh ra)</summary>
    private static readonly string[] NewTables = { "Branches", "UserBranches", "ProductStocks", "StockTransfers", "StockTransferLines", "Languages", "TranslationKeys", "Translations" };

    /// <summary>Bảng nghiệp vụ được thêm cột BranchId</summary>
    private static readonly string[] ScopedTables =
        { "Owners", "Appointments", "Visits", "Vaccinations", "Cages", "Admissions", "Invoices", "StockReceipts", "ProductBatches", "StockMoves" };

    public static async Task<int> RunAsync(PetDbContext db, ILogger? log = null)
    {
        // 1. Bảng mới
        var missing = new List<string>();
        foreach (var t in NewTables) if (!await TableExistsAsync(db, t)) missing.Add(t);
        if (missing.Count > 0)
        {
            log?.LogInformation("Nâng cấp chuỗi chi nhánh: tạo bảng {Tables}", string.Join(", ", missing));
            foreach (var sql in Statements(db.Database.GenerateCreateScript()))
            {
                var m = Regex.Match(sql, @"^CREATE\s+TABLE\s+\[(\w+)\]|^CREATE\s+(?:UNIQUE\s+)?INDEX\s+\[[^\]]+\]\s+ON\s+\[(\w+)\]", RegexOptions.IgnoreCase);
                var table = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (m.Success && missing.Contains(table)) await db.Database.ExecuteSqlRawAsync(sql);
            }
        }

        // 2. Chi nhánh mặc định
        if (!await db.Branches.AnyAsync())
        {
            var settings = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
            string? S(string key) => settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            db.Branches.Add(new Branch
            {
                Code = "CN01", Name = S(SettingsService.ClinicName) is { } n ? n.Truncate(150) : "Chi nhánh chính",
                Address = S(SettingsService.ClinicAddress)?.Truncate(300), Phone = S(SettingsService.ClinicPhone)?.Truncate(30),
                Email = S(SettingsService.ClinicEmail)?.Truncate(150), SortOrder = 1
            });
            await db.SaveChangesAsync();
        }
        var def = await db.Branches.OrderBy(b => b.SortOrder).ThenBy(b => b.Id).Select(b => b.Id).FirstAsync();

        // 3. Cột BranchId cho dữ liệu nghiệp vụ cũ
        foreach (var t in ScopedTables)
        {
            if (await ColumnExistsAsync(db, t, "BranchId")) continue;
            log?.LogInformation("Nâng cấp chuỗi chi nhánh: thêm {Table}.BranchId (gán chi nhánh {Branch})", t, def);
            await Exec(db, $"ALTER TABLE [{t}] ADD [BranchId] int NULL");
            await Exec(db, $"UPDATE [{t}] SET [BranchId] = {def} WHERE [BranchId] IS NULL");
            await Exec(db, $"ALTER TABLE [{t}] ALTER COLUMN [BranchId] int NOT NULL");
            await Exec(db, $"ALTER TABLE [{t}] ADD CONSTRAINT [FK_{t}_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id])");
            if (t == "Cages")
            {
                await Exec(db, "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cages_Code' AND object_id = OBJECT_ID('Cages')) DROP INDEX [IX_Cages_Code] ON [Cages]");
                await Exec(db, "CREATE UNIQUE INDEX [IX_Cages_BranchId_Code] ON [Cages] ([BranchId], [Code])");
            }
            else await Exec(db, $"CREATE INDEX [IX_{t}_BranchId] ON [{t}] ([BranchId])");
        }

        // Ngôn ngữ giao diện của người dùng
        if (!await ColumnExistsAsync(db, "Users", "Language")) await Exec(db, "ALTER TABLE [Users] ADD [Language] nvarchar(20) NULL");

        // 4. Người dùng: quyền toàn chuỗi + gán chi nhánh mặc định
        if (!await ColumnExistsAsync(db, "Users", "AllBranches"))
        {
            await Exec(db, "ALTER TABLE [Users] ADD [AllBranches] bit NOT NULL CONSTRAINT [DF_Users_AllBranches] DEFAULT CAST(0 AS bit)");
            await Exec(db, $"UPDATE [Users] SET [AllBranches] = 1 WHERE ',' + REPLACE([Roles], ' ', '') + ',' LIKE '%,{AppRoles.Admin},%'");
        }
        if (missing.Contains("UserBranches"))
            await Exec(db, $"INSERT INTO [UserBranches] ([UserId], [BranchId]) SELECT [Id], {def} FROM [Users]");

        // 5. Tồn kho: chuyển cột Products.Stock (tồn chung) sang tồn của chi nhánh mặc định
        if (await ColumnExistsAsync(db, "Products", "Stock"))
        {
            if (missing.Contains("ProductStocks"))
                await Exec(db, $"INSERT INTO [ProductStocks] ([BranchId], [ProductId], [Quantity], [CostPrice]) SELECT {def}, [Id], [Stock], [CostPrice] FROM [Products]");
            // Cột cũ giữ lại làm bản lưu, chỉ thêm giá trị mặc định để thêm mặt hàng mới không lỗi
            await Exec(db, "IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('Products') AND COL_NAME(parent_object_id, parent_column_id) = 'Stock') " +
                           "ALTER TABLE [Products] ADD CONSTRAINT [DF_Products_Stock] DEFAULT 0 FOR [Stock]");
        }
        return def;
    }

    /// <summary>Tách script EF thành từng lệnh (phân cách bởi GO hoặc dòng trống sau dấu ;)</summary>
    private static IEnumerable<string> Statements(string script)
    {
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline);
        foreach (var batch in batches)
            foreach (var part in Regex.Split(batch, @";\s*\r?\n\s*\r?\n"))
            {
                var sql = part.Trim();
                if (sql.Length > 0) yield return sql.TrimEnd(';') + ";";
            }
    }

    private static Task<int> Exec(PetDbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    private static async Task<bool> TableExistsAsync(PetDbContext db, string table) =>
        await db.Database.SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID({table}, N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync() == 1;

    private static async Task<bool> ColumnExistsAsync(PetDbContext db, string table, string column) =>
        await db.Database.SqlQuery<int>($"SELECT CASE WHEN COL_LENGTH({table}, {column}) IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync() == 1;
}
