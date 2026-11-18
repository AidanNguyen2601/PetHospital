using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PetHospital.Web.Data;
using PetHospital.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Kiểm tra câu lệnh của báo cáo động: chỉ cho phép một câu SELECT (hoặc WITH … SELECT), không ghi / sửa cấu trúc / gọi thủ tục.
/// Đây là lớp chặn thứ nhất; khi chạy còn được bọc trong giao dịch luôn hoàn tác, giới hạn thời gian và số dòng.
/// Khuyến nghị cấu hình thêm ConnectionStrings:ReportConnection bằng tài khoản SQL chỉ có quyền đọc (db_datareader).
/// </summary>
public static class SqlGuard
{
    private static readonly Regex Comments = new(@"--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Strings = new(@"N?'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex QuotedIds = new(@"\[(?:[^\]]|\]\])*\]|""(?:[^""]|"""")*""", RegexOptions.Compiled);
    private static readonly Regex Blocked = new(
        @"\b(insert|update|delete|merge|drop|alter|create|truncate|exec|execute|grant|revoke|deny|backup|restore|shutdown|kill|dbcc|reconfigure|" +
        @"bulk|openrowset|opendatasource|openquery|openxml|into|waitfor|use|declare|set|begin|commit|rollback|save|transaction|cursor|" +
        @"raiserror|throw|print|readtext|writetext|updatetext|checkpoint|sp_\w*|xp_\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Secret = new(@"\bPasswordHash\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ParamRef = new(@"(?<![@\w])@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>Bỏ chú thích và chuỗi ký tự (giữ tên cột trong [ ])</summary>
    private static string Strip(string sql) => Strings.Replace(Comments.Replace(sql, " "), "''");

    /// <summary>Kiểm tra câu lệnh đầy đủ; trả thông báo lỗi hoặc null nếu hợp lệ</summary>
    public static string? CheckQuery(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "Chưa nhập câu lệnh SQL.";
        if (sql.Length > 20000) return "Câu lệnh quá dài (tối đa 20.000 ký tự).";
        return CheckCore(Strip(sql), fragment: false);
    }

    /// <summary>Kiểm tra một đoạn điều kiện (WHERE / ORDER BY / danh sách cột) ghép vào câu lệnh của View</summary>
    public static string? CheckFragment(string? text, string what)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Contains("--") || text.Contains("/*")) return $"{what}: không dùng chú thích (-- hoặc /* */).";
        var err = CheckCore(Strings.Replace(text, "''"), fragment: true);
        return err == null ? null : $"{what}: {err}";
    }

    private static string? CheckCore(string stripped, bool fragment)
    {
        if (Secret.IsMatch(stripped)) return "Không được truy xuất cột mật khẩu (PasswordHash).";
        var s = QuotedIds.Replace(stripped, "[]").Trim();
        if (fragment ? s.Contains(';') : s.TrimEnd(';', ' ', '\r', '\n', '\t').Contains(';'))
            return "Chỉ được dùng một câu lệnh (không có dấu ; ở giữa).";
        if (!fragment && !Regex.IsMatch(s, @"^\(*\s*(select|with)\b", RegexOptions.IgnoreCase))
            return "Câu lệnh phải bắt đầu bằng SELECT hoặc WITH (chỉ truy xuất dữ liệu).";
        var m = Blocked.Match(s);
        if (m.Success) return $"Không được dùng từ khóa \"{m.Value.ToUpperInvariant()}\" – báo cáo động chỉ được đọc dữ liệu.";
        return null;
    }

    /// <summary>Các tham số @Ten được dùng trong câu lệnh (theo thứ tự xuất hiện, không trùng)</summary>
    public static List<string> ParamNames(string? sql) =>
        string.IsNullOrEmpty(sql) ? new() :
        ParamRef.Matches(Strip(sql)).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Đặt tên cột / view trong [ ] (đã kiểm tra tồn tại trước khi dùng)</summary>
    public static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";
}

/// <summary>Kết quả chạy báo cáo động</summary>
public sealed class DynamicResult
{
    public List<string> Columns { get; } = new();
    public List<Type> Types { get; } = new();
    public List<object?[]> Rows { get; } = new();
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    public string Sql { get; set; } = "";

    public bool IsNumeric(int i) => Types[i] == typeof(int) || Types[i] == typeof(long) || Types[i] == typeof(short) || Types[i] == typeof(byte)
                                    || Types[i] == typeof(decimal) || Types[i] == typeof(double) || Types[i] == typeof(float);
}

/// <summary>Thông tin một cột trong View / bảng (trình duyệt cấu trúc dữ liệu)</summary>
public record DbObjectInfo(string Schema, string Name, bool IsView, List<(string Column, string Type)> Columns)
{
    public string FullName => $"{Schema}.{Name}";
}

/// <summary>Chạy báo cáo động ở chế độ chỉ đọc</summary>
public class DynamicReportEngine
{
    public const int MaxRows = 10000;
    public const int TimeoutSeconds = 60;
    /// <summary>Tham số có sẵn: kỳ báo cáo, chi nhánh, người dùng</summary>
    public static readonly string[] BuiltIn = { "From", "To", "ToEnd", "BranchId", "UserId" };

    private readonly PetDbContext _db;
    private readonly IConfiguration _cfg;
    public DynamicReportEngine(PetDbContext db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    /// <summary>Đang dùng tài khoản riêng chỉ đọc (ConnectionStrings:ReportConnection)</summary>
    public bool HasReadOnlyConnection => !string.IsNullOrWhiteSpace(_cfg.GetConnectionString("ReportConnection"));

    private SqlConnection Open() => new(HasReadOnlyConnection ? _cfg.GetConnectionString("ReportConnection") : _db.Database.GetConnectionString());

    public static List<ReportParam> Params(DynamicReport r)
    {
        if (string.IsNullOrWhiteSpace(r.ParametersJson)) return new();
        try { return JsonSerializer.Deserialize<List<ReportParam>>(r.ParametersJson) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Tham số được dùng trong báo cáo (câu SQL, hoặc điều kiện lọc của View)</summary>
    public static List<string> UsedParams(DynamicReport r) =>
        SqlGuard.ParamNames(r.Source == ReportSource.Sql ? r.SqlText : r.WhereClause + " " + r.OrderBy + " " + r.Columns);

    public static bool UsesDates(DynamicReport r) =>
        r.Source == ReportSource.View ? !string.IsNullOrEmpty(r.DateColumn) : UsedParams(r).Any(p => p is "From" or "To" or "ToEnd");

    public static bool UsesBranch(DynamicReport r) =>
        r.Source == ReportSource.View ? !string.IsNullOrEmpty(r.BranchColumn) : UsedParams(r).Any(p => p == "BranchId");

    /// <summary>Đồng bộ danh sách tham số tự khai báo với câu lệnh: giữ định nghĩa cũ, thêm tham số mới, bỏ tham số không còn dùng</summary>
    public static List<ReportParam> SyncParams(DynamicReport r, IEnumerable<ReportParam>? posted)
    {
        var old = (posted ?? Enumerable.Empty<ReportParam>()).Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        return UsedParams(r).Where(n => !BuiltIn.Contains(n, StringComparer.OrdinalIgnoreCase)).Select(n =>
        {
            var p = old.FirstOrDefault(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase)) ?? new ReportParam
            {
                Name = n, Label = n,
                Type = Regex.IsMatch(n, "ngay|date|tu|den", RegexOptions.IgnoreCase) ? "date" : Regex.IsMatch(n, "^(so|id)|count|thang|nam", RegexOptions.IgnoreCase) ? "int" : "text"
            };
            p.Name = n;
            p.Label = string.IsNullOrWhiteSpace(p.Label) ? n : p.Label.Trim().Truncate(150);
            if (p.Type is not ("text" or "int" or "decimal" or "date" or "select")) p.Type = "text";
            return p;
        }).ToList();
    }

    /// <summary>Giá trị mặc định của tham số (ngày: today, monthstart, yearstart, -30 / +30 so với hôm nay)</summary>
    public static string DefaultText(ReportParam p)
    {
        var d = (p.Default ?? "").Trim();
        if (p.Type != "date") return d;
        var today = DateTime.Today;
        DateTime? v = d.ToLowerInvariant() switch
        {
            "" => null,
            "today" => today,
            "monthstart" => new DateTime(today.Year, today.Month, 1),
            "yearstart" => new DateTime(today.Year, 1, 1),
            _ when int.TryParse(d, out var n) => today.AddDays(n),
            _ when DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) => x,
            _ => null
        };
        return v?.ToString("yyyy-MM-dd") ?? "";
    }

    /// <summary>Đổi giá trị nhập (chuỗi) sang kiểu của tham số; lỗi → thông báo</summary>
    public static object? Convert(ReportParam p, string? text, out string? error)
    {
        error = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return p.Type is "text" or "select" ? "" : null;
        switch (p.Type)
        {
            case "int":
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                break;
            case "decimal":
                if (decimal.TryParse(text.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var m)) return m;
                break;
            case "date":
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt.Date;
                break;
            default:
                return text.Truncate(500);
        }
        error = $"Giá trị \"{text}\" không hợp lệ cho tham số {p.Label}.";
        return null;
    }

    /// <summary>Danh sách lựa chọn của tham số select: (giá trị, nhãn)</summary>
    public static List<(string Value, string Label)> Options(ReportParam p) =>
        (p.Options ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => { var i = line.IndexOf('='); return i < 0 ? (line, line) : (line[..i].Trim(), line[(i + 1)..].Trim()); }).ToList();

    // ===================== Cấu trúc database =====================

    /// <summary>View và bảng cùng các cột (ẩn cột mật khẩu)</summary>
    public async Task<List<DbObjectInfo>> SchemaAsync()
    {
        const string sql = @"SELECT t.TABLE_SCHEMA, t.TABLE_NAME, t.TABLE_TYPE, c.COLUMN_NAME, c.DATA_TYPE
FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
WHERE t.TABLE_NAME NOT LIKE '[_][_]%' ORDER BY CASE WHEN t.TABLE_TYPE = 'VIEW' THEN 0 ELSE 1 END, t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";
        var map = new Dictionary<string, DbObjectInfo>();
        var list = new List<DbObjectInfo>();
        await using var conn = Open();
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var key = rd.GetString(0) + "." + rd.GetString(1);
            if (!map.TryGetValue(key, out var o))
            {
                o = new DbObjectInfo(rd.GetString(0), rd.GetString(1), rd.GetString(2) == "VIEW", new());
                map[key] = o; list.Add(o);
            }
            if (!rd.GetString(3).Equals("PasswordHash", StringComparison.OrdinalIgnoreCase)) o.Columns.Add((rd.GetString(3), rd.GetString(4)));
        }
        return list;
    }

    // ===================== Dựng & chạy câu lệnh =====================

    /// <summary>Kiểm tra định nghĩa báo cáo; trả lỗi hoặc null</summary>
    public async Task<string?> ValidateAsync(DynamicReport r)
    {
        if (r.Source == ReportSource.Sql) return SqlGuard.CheckQuery(r.SqlText);
        if (string.IsNullOrWhiteSpace(r.ViewName)) return "Chưa chọn View.";
        var view = await FindViewAsync(r.ViewName);
        if (view == null) return $"Không tìm thấy View \"{r.ViewName}\".";
        var cols = view.Columns.Select(c => c.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, label) in new[] { (r.DateColumn, "Cột lọc theo ngày"), (r.BranchColumn, "Cột chi nhánh") })
            if (!string.IsNullOrEmpty(col) && !cols.Contains(col)) return $"{label}: View không có cột \"{col}\".";
        foreach (var c in SplitColumns(r.Columns))
            if (!cols.Contains(c)) return $"Cột hiển thị: View không có cột \"{c}\".";
        return SqlGuard.CheckFragment(r.WhereClause, "Điều kiện lọc") ?? SqlGuard.CheckFragment(r.OrderBy, "Sắp xếp");
    }

    public static List<string> SplitColumns(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.Trim('[', ']')).Where(c => c.Length > 0).ToList();

    private async Task<DbObjectInfo?> FindViewAsync(string name)
    {
        var parts = name.Replace("[", "").Replace("]", "").Split('.', 2);
        var (schema, view) = parts.Length == 2 ? (parts[0], parts[1]) : ("dbo", parts[0]);
        return (await SchemaAsync()).FirstOrDefault(o => o.IsView
            && o.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) && o.Name.Equals(view, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Câu lệnh thực thi (đã kiểm tra bằng ValidateAsync)</summary>
    public async Task<string> BuildSqlAsync(DynamicReport r, bool filterBranch)
    {
        if (r.Source == ReportSource.Sql) return r.SqlText!.Trim().TrimEnd(';');
        var view = (await FindViewAsync(r.ViewName!))!;
        var cols = SplitColumns(r.Columns);
        var sql = $"SELECT {(cols.Count == 0 ? "*" : string.Join(", ", cols.Select(SqlGuard.Quote)))} FROM {SqlGuard.Quote(view.Schema)}.{SqlGuard.Quote(view.Name)} WHERE 1 = 1";
        if (!string.IsNullOrEmpty(r.DateColumn)) sql += $" AND {SqlGuard.Quote(r.DateColumn)} >= @From AND {SqlGuard.Quote(r.DateColumn)} < @ToEnd";
        if (!string.IsNullOrEmpty(r.BranchColumn) && filterBranch) sql += $" AND {SqlGuard.Quote(r.BranchColumn)} = @BranchId";
        if (!string.IsNullOrWhiteSpace(r.WhereClause)) sql += $" AND ({r.WhereClause.Trim()})";
        if (!string.IsNullOrWhiteSpace(r.OrderBy)) sql += $" ORDER BY {r.OrderBy.Trim()}";
        return sql;
    }

    /// <summary>Chạy câu lệnh trong giao dịch luôn hoàn tác (không thể ghi dữ liệu), giới hạn thời gian và số dòng</summary>
    public async Task<DynamicResult> RunAsync(string sql, IDictionary<string, object?> parameters, int maxRows = MaxRows, CancellationToken ct = default)
    {
        var result = new DynamicResult { Sql = sql };
        var sw = Stopwatch.StartNew();
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await ReadAsync(conn, tx, sql, parameters, maxRows, result, ct);
        }
        catch (SqlException) when (result.Truncated) { }   // hủy lệnh khi đã đủ số dòng tối đa
        finally
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch (InvalidOperationException) { } catch (SqlException) { }
        }
        result.ElapsedMs = sw.ElapsedMilliseconds;
        return result;
    }

    private static async Task ReadAsync(SqlConnection conn, SqlTransaction tx, string sql, IDictionary<string, object?> parameters, int maxRows,
        DynamicResult result, CancellationToken ct)
    {
        {
            await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = TimeoutSeconds };
            foreach (var (name, value) in parameters)
            {
                var p = cmd.Parameters.AddWithValue("@" + name, value ?? DBNull.Value);
                if (value is string) p.SqlDbType = SqlDbType.NVarChar;
                else if (value is DateTime) p.SqlDbType = SqlDbType.DateTime2;
                else if (value == null && name == "BranchId") p.SqlDbType = SqlDbType.Int;
            }
            await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
            for (var i = 0; i < rd.FieldCount; i++)
            {
                var n = rd.GetName(i);
                result.Columns.Add(string.IsNullOrEmpty(n) ? $"Cột {i + 1}" : n);
                result.Types.Add(rd.GetFieldType(i));
            }
            while (await rd.ReadAsync(ct))
            {
                if (result.Rows.Count >= maxRows) { result.Truncated = true; cmd.Cancel(); break; }
                var row = new object?[rd.FieldCount];
                for (var i = 0; i < rd.FieldCount; i++)
                {
                    var v = rd.IsDBNull(i) ? null : rd.GetValue(i);
                    row[i] = v switch { byte[] b => $"(nhị phân {b.Length} byte)", Guid g => g.ToString(), TimeSpan t => t.ToString(@"hh\:mm"), _ => v };
                }
                result.Rows.Add(row);
            }
        }
    }
}
