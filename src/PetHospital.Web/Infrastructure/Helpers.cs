using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PetHospital.Web.Infrastructure;

public static class FormatExtensions
{
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Định dạng tiền VNĐ: 15.000.000</summary>
    public static string ToMoney(this decimal value) => value.ToString("N0", Vi);
    public static string ToMoney(this decimal? value) => value.HasValue ? value.Value.ToString("N0", Vi) : "";

    /// <summary>Số lượng: bỏ phần thập phân thừa</summary>
    public static string ToQty(this decimal value) => value.ToString("0.##", Vi);
    public static string ToQty(this decimal? value) => value.HasValue ? value.Value.ToString("0.##", Vi) : "";

    public static string ToShortMoney(this decimal value) =>
        value >= 1_000_000_000 ? (value / 1_000_000_000m).ToString("0.##", Vi) + " tỷ"
        : value >= 1_000_000 ? (value / 1_000_000m).ToString("0.#", Vi) + " tr"
        : value.ToString("N0", Vi);

    public static string ToDate(this DateTime value) => value.ToString("dd/MM/yyyy");
    public static string ToDate(this DateTime? value) => value?.ToString("dd/MM/yyyy") ?? "";
    public static string ToDateTime(this DateTime value) => value.ToString("dd/MM/yyyy HH:mm");
    public static string ToDateTime(this DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm") ?? "";

    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }

    /// <summary>Danh sách chọn từ enum (tên hiển thị tiếng Việt)</summary>
    public static List<SelectListItem> EnumOptions<T>(T? selected = null, bool blank = false, string blankText = "-- Tất cả --") where T : struct, Enum
    {
        var list = Enum.GetValues<T>().Select(v => new SelectListItem(v.GetDisplayName(), Convert.ToInt32(v).ToString(),
            selected.HasValue && EqualityComparer<T>.Default.Equals(selected.Value, v))).ToList();
        if (blank) list.Insert(0, new SelectListItem(blankText, ""));
        return list;
    }

    public static string Truncate(this string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Màu badge theo trạng thái</summary>
public static class Badges
{
    public static string For(AppointmentStatus s) => s switch
    {
        AppointmentStatus.Booked => "secondary",
        AppointmentStatus.Confirmed => "info text-dark",
        AppointmentStatus.CheckedIn => "warning text-dark",
        AppointmentStatus.Completed => "success",
        AppointmentStatus.NoShow => "dark",
        _ => "light text-dark border"
    };

    public static string For(VisitStatus s) => s switch
    {
        VisitStatus.Waiting => "warning text-dark",
        VisitStatus.InProgress => "primary",
        VisitStatus.Completed => "success",
        _ => "light text-dark border"
    };

    public static string For(InvoiceStatus s) => s switch
    {
        InvoiceStatus.Draft => "secondary",
        InvoiceStatus.Unpaid => "danger",
        InvoiceStatus.Partial => "warning text-dark",
        InvoiceStatus.Paid => "success",
        _ => "light text-dark border"
    };

    public static string For(DocState s) => s switch
    {
        DocState.Draft => "secondary",
        DocState.Posted => "success",
        _ => "light text-dark border"
    };

    public static string For(AdmissionStatus s) => s switch
    {
        AdmissionStatus.Active => "primary",
        AdmissionStatus.Discharged => "success",
        _ => "light text-dark border"
    };

    public static string For(StockMoveType t) => t switch
    {
        StockMoveType.Receipt or StockMoveType.AdjustIn or StockMoveType.Return or StockMoveType.TransferIn => "success",
        _ => "danger"
    };
}

public static class ClaimsExtensions
{
    public const string FullNameClaim = "FullName";
    public const string VetClaim = "vet";

    public static int GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string GetFullName(this ClaimsPrincipal user) =>
        user.FindFirstValue(FullNameClaim) ?? user.Identity?.Name ?? "";
}

public static class Paging
{
    public static readonly int[] PageSizes = { 10, 20, 50, 100, 200 };
    public const int DefaultPageSize = 20;
    public static int Clamp(int size) => size <= 0 ? DefaultPageSize : Math.Min(size, PageSizes[^1]);
}

public interface IPagedList
{
    int Page { get; }
    int PageSize { get; }
    int TotalCount { get; }
    int TotalPages { get; }
}

public class PagedList<T> : IPagedList
{
    public IReadOnlyList<T> Items { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PagedList(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items; TotalCount = totalCount; Page = page; PageSize = pageSize;
    }
}

public static class TokenGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Chuỗi ngẫu nhiên an toàn (không có ký tự dễ nhầm 0/O, 1/I)</summary>
    public static string Random(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
