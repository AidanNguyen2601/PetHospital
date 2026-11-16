using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PetHospital.Web.Infrastructure;

/// <summary>
/// Trang Razor tự dịch phần HTML tĩnh (chữ, title, placeholder, data-confirm…) theo ngôn ngữ đang dùng.
/// Văn bản trong biểu thức C# dùng T("...") hoặc Tx(chuỗi động).
/// </summary>
public abstract class LocalizedRazorPage<TModel> : RazorPage<TModel>
{
    private bool _inScript;
    private string? _attr;

    public override void WriteLiteral(string? value)
    {
        var l = Localizer.Instance;
        if (value == null || l == null || !l.IsTranslating) { base.WriteLiteral(value); return; }
        base.WriteLiteral(l.Html(value, ref _inScript));
    }

    /// <summary>Chuỗi in bằng @(...) (nhãn trạng thái, tiêu đề cột báo cáo…): chỉ dịch khi khớp đúng câu đã có bản dịch</summary>
    public override void Write(string? value)
    {
        if (value != null && value.Length <= 400 && Localizer.Instance is { IsTranslating: true } l) value = l.Known(value);
        base.Write(value);
    }

    public override void Write(object? value)
    {
        if (value is string s) { Write(s); return; }
        base.Write(value);
    }

    public override void BeginWriteAttribute(string name, string prefix, int prefixOffset, string suffix, int suffixOffset, int attributeValuesCount)
    {
        _attr = name;
        base.BeginWriteAttribute(name, prefix, prefixOffset, suffix, suffixOffset, attributeValuesCount);
    }

    // Không phải virtual: view sinh ra gọi trực tiếp trên lớp con nên phương thức che (new) vẫn được dùng
    public new void WriteAttributeValue(string prefix, int prefixOffset, object? value, int valueOffset, int valueLength, bool isLiteral)
    {
        // Phần chữ cố định của thuộc tính hiển thị có biểu thức @: data-confirm="Xóa @x?"
        if (isLiteral && value is string s && _attr is "title" or "placeholder" or "data-confirm" or "aria-label" or "alt" or "label"
            && Localizer.Instance is { IsTranslating: true } l)
            value = l.Text(s);
        base.WriteAttributeValue(prefix, prefixOffset, value, valueOffset, valueLength, isLiteral);
    }

    public override void EndWriteAttribute()
    {
        _attr = null;
        base.EndWriteAttribute();
    }

    /// <summary>Dịch khóa (văn bản gốc tiếng Việt)</summary>
    public string T(string key) => L.T(key);
    public string T(string key, params object?[] args) => L.T(key, args);
    /// <summary>Dịch văn bản động (thông báo, tiêu đề ghép mã)</summary>
    public string Tx(string? s) => L.Text(s);
}

/// <summary>Dịch thuộc tính hiển thị trên các thẻ do tag helper xử lý (form asp-action data-confirm, input asp-for placeholder…)</summary>
[HtmlTargetElement("*", Attributes = "data-confirm")]
[HtmlTargetElement("*", Attributes = "placeholder")]
[HtmlTargetElement("*", Attributes = "title")]
[HtmlTargetElement("*", Attributes = "aria-label")]
public sealed class TranslateAttributesTagHelper : TagHelper
{
    private static readonly string[] Names = { "data-confirm", "placeholder", "title", "aria-label" };
    public override int Order => 10000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Localizer.Instance is not { IsTranslating: true } l) return;
        foreach (var n in Names)
        {
            if (!output.Attributes.TryGetAttribute(n, out var a) || a.Value == null) continue;
            var raw = a.Value switch
            {
                string s => s,
                HtmlString hs => System.Net.WebUtility.HtmlDecode(hs.Value ?? ""),
                IHtmlContent hc => System.Net.WebUtility.HtmlDecode(Render(hc)),
                _ => a.Value.ToString() ?? ""
            };
            var tr = l.Text(raw, collect: false);
            if (tr != raw) output.Attributes.SetAttribute(n, tr);
        }
    }

    private static string Render(IHtmlContent c)
    {
        using var w = new StringWriter();
        c.WriteTo(w, System.Text.Encodings.Web.HtmlEncoder.Default);
        return w.ToString();
    }
}

/// <summary>Dịch thông báo lỗi kiểm tra dữ liệu (ModelState) hiển thị bởi asp-validation-for / asp-validation-summary</summary>
[HtmlTargetElement("span", Attributes = "asp-validation-for")]
[HtmlTargetElement("div", Attributes = "asp-validation-summary")]
public sealed class TranslateValidationTagHelper : TagHelper
{
    public override int Order => 10000;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (Localizer.Instance is not { IsTranslating: true } l) return;
        var html = output.Content.IsModified ? output.Content.GetContent() : (await output.GetChildContentAsync()).GetContent();
        if (string.IsNullOrEmpty(html)) return;
        var inScript = false;
        output.Content.SetHtmlContent(l.Html(html, ref inScript, decodeEntities: true));
    }
}
