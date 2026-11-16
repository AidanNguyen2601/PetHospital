using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Đọc / ghi bảng tính không cần thư viện ngoài: Excel .xlsx (sheet đầu tiên) và CSV / TXT.
/// Giá trị ô: string, số (int / decimal / double), hoặc null.
/// </summary>
public static class Spreadsheet
{
    // =========================== Ghi ===========================

    /// <summary>File .xlsx 1 sheet: dòng đầu là tiêu đề (in đậm, cố định khi cuộn).</summary>
    public static byte[] ToXlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var data = rows.ToList();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            Entry("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Entry("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            var safeName = new string(sheetName.Where(c => !"[]:*?/\\".Contains(c)).Take(31).ToArray());
            Entry("xl/workbook.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Xml(safeName.Length > 0 ? safeName : "Sheet1")}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Entry("xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE7EFFF"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="2"><xf/><xf fontId="1" fillId="2" applyFont="1" applyFill="1"/></cellXfs></styleSheet>""");

            // Độ rộng cột theo nội dung (giới hạn 8–60 ký tự)
            var widths = headers.Select(h => (double)Math.Max(8, h.Length + 2)).ToArray();
            foreach (var r in data.Take(500))
                for (var i = 0; i < Math.Min(r.Count, widths.Length); i++)
                    widths[i] = Math.Min(60, Math.Max(widths[i], (Format(r[i])?.Length ?? 0) + 2));

            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
            sb.Append("<cols>");
            for (var i = 0; i < widths.Length; i++) sb.Append($"""<col min="{i + 1}" max="{i + 1}" width="{widths[i].ToString(CultureInfo.InvariantCulture)}" customWidth="1"/>""");
            sb.Append("</cols><sheetData>");
            AppendRow(sb, 1, headers.Cast<object?>().ToList(), style: 1);
            var n = 2;
            foreach (var r in data) AppendRow(sb, n++, r, style: 0);
            sb.Append("</sheetData>");
            if (headers.Count > 0) sb.Append($"""<autoFilter ref="A1:{ColName(headers.Count - 1)}{Math.Max(1, data.Count + 1)}"/>""");
            sb.Append("</worksheet>");
            Entry("xl/worksheets/sheet1.xml", sb.ToString());
        }
        return ms.ToArray();
    }

    private static void AppendRow(StringBuilder sb, int rowNo, IReadOnlyList<object?> cells, int style)
    {
        sb.Append($"""<row r="{rowNo}">""");
        for (var i = 0; i < cells.Count; i++)
        {
            var v = cells[i];
            if (v == null) continue;
            var reference = ColName(i) + rowNo;
            var s = style > 0 ? $" s=\"{style}\"" : "";
            switch (v)
            {
                case int or long or decimal or double or float:
                    sb.Append($"""<c r="{reference}"{s}><v>{Convert.ToString(v, CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                default:
                    var text = Format(v) ?? "";
                    if (text.Length == 0) continue;
                    sb.Append($"""<c r="{reference}"{s} t="inlineStr"><is><t xml:space="preserve">{Xml(text)}</t></is></c>""");
                    break;
            }
        }
        sb.Append("</row>");
    }

    /// <summary>CSV UTF-8 có BOM (Excel mở đúng tiếng Việt); chặn công thức ở ô bắt đầu bằng = + - @.</summary>
    public static byte[] ToCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(h => Csv(h))));
        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Select(c => c is int or long or decimal or double
                ? Convert.ToString(c, CultureInfo.InvariantCulture) : Csv(Format(c) ?? ""))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static string Csv(string s)
    {
        if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>Định dạng giá trị thành chữ (ngày dd/MM/yyyy [HH:mm], có/không, enum theo tên hiển thị).</summary>
    public static string? Format(object? v) => v switch
    {
        null => null,
        string s => s,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd/MM/yyyy") : d.ToString(d.Second == 0 ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy HH:mm:ss"),
        TimeSpan t => t.ToString(@"hh\:mm"),
        bool b => b ? "Có" : "Không",
        Enum e => e.GetDisplayName(),
        decimal m => m.ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };

    private static string ColName(int index)
    {
        var s = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    /// <summary>Thoát ký tự XML và bỏ ký tự điều khiển không hợp lệ.</summary>
    private static string Xml(string s)
    {
        var clean = new string(s.Where(XmlConvert.IsXmlChar).ToArray());
        return System.Security.SecurityElement.Escape(clean) ?? "";
    }

    // =========================== Đọc ===========================

    /// <summary>Đọc các dòng: .xlsx (sheet đầu tiên) hoặc văn bản (.csv / .txt / .dat; phân cách tab, chấm phẩy hoặc dấu phẩy).</summary>
    public static List<List<string>> Read(Stream stream, string fileName) =>
        Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(stream) : ReadText(stream);

    private static List<List<string>> ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = new List<List<string>>();
        var text = reader.ReadToEnd();
        var lines = text.Split('\n');
        var sep = lines.FirstOrDefault(l => l.Trim().Length > 0) is string first
            ? (first.Contains('\t') ? '\t' : first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',')
            : ',';
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(SplitCsv(line, sep));
        }
        return rows;
    }

    /// <summary>Tách 1 dòng CSV có hỗ trợ ô trong dấu nháy kép.</summary>
    private static List<string> SplitCsv(string line, char sep)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"' && sb.Length == 0) quoted = true;
            else if (c == sep) { cells.Add(Clean(sb.ToString())); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(Clean(sb.ToString()));
        return cells;
        // Ô xuất từ hệ thống có thể được thêm dấu ' để chặn công thức
        static string Clean(string s) { s = s.Trim(); return s.Length > 1 && s[0] == '\'' && "=+-@".Contains(s[1]) ? s[1..] : s; }
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        XDocument Load(string path) { using var s = zip.GetEntry(path)!.Open(); return XDocument.Load(s); }

        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? new List<string>()
            : Load("xl/sharedStrings.xml").Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList();

        var sheetPath = "xl/worksheets/sheet1.xml";
        var firstSheet = zip.GetEntry("xl/workbook.xml") == null ? null : Load("xl/workbook.xml").Descendants(ns + "sheet").FirstOrDefault();
        var rid = firstSheet?.Attribute(rel + "id")?.Value;
        if (rid != null && zip.GetEntry("xl/_rels/workbook.xml.rels") != null)
        {
            var target = Load("xl/_rels/workbook.xml.rels").Root!.Elements()
                .FirstOrDefault(r => r.Attribute("Id")?.Value == rid)?.Attribute("Target")?.Value;
            if (target != null) sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        if (zip.GetEntry(sheetPath) == null) return new();

        var result = new List<List<string>>();
        foreach (var row in Load(sheetPath).Descendants(ns + "row"))
        {
            var cells = new List<string>();
            foreach (var c in row.Elements(ns + "c"))
            {
                var idx = ColumnIndex(c.Attribute("r")?.Value);
                while (cells.Count < idx) cells.Add("");
                var type = c.Attribute("t")?.Value;
                var v = c.Element(ns + "v")?.Value ?? "";
                var text = type switch
                {
                    "s" when int.TryParse(v, out var i) && i < shared.Count => shared[i],
                    "inlineStr" => string.Concat(c.Descendants(ns + "t").Select(t => t.Value)),
                    _ => v
                };
                cells.Add(text.Trim());
            }
            result.Add(cells);
        }
        return result;
    }

    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        var n = 0;
        foreach (var ch in cellRef.TakeWhile(char.IsLetter)) n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return Math.Max(0, n - 1);
    }
}
