using System.Security.Claims;
using System.Text.Json;
using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace PetHospital.Web.Data;

public class PetDbContext : DbContext
{
    private readonly IHttpContextAccessor _http;
    private bool _writingAudit;

    public PetDbContext(DbContextOptions<PetDbContext> options, IHttpContextAccessor http) : base(options) => _http = http;

    // ================= Chi nhánh đang làm việc =================

    private int? _branchId;
    private bool _branchResolved;
    private string? _branchCode;

    /// <summary>
    /// Chi nhánh dùng để lọc dữ liệu (bộ lọc toàn cục). Mặc định lấy theo người dùng đăng nhập;
    /// null = không lọc (khởi tạo dữ liệu, báo cáo toàn chuỗi).
    /// </summary>
    public int? CurrentBranchId
    {
        get
        {
            if (!_branchResolved) { _branchId = BranchScope.Resolve(_http.HttpContext); _branchResolved = true; }
            return _branchId;
        }
        set { _branchId = value; _branchResolved = true; _branchCode = null; }
    }

    /// <summary>Chi nhánh gán cho bản ghi mới khi không lọc theo chi nhánh (khởi tạo dữ liệu mẫu, mô phỏng từ dòng lệnh)</summary>
    public int? DefaultBranchId { get; set; }

    /// <summary>Khách hàng dùng chung toàn chuỗi (đọc từ cài đặt)</summary>
    public bool ShareCustomers => ChainSettings.ShareCustomers;

    /// <summary>Chi nhánh ghi dữ liệu mới</summary>
    public int WriteBranchId => CurrentBranchId is > 0 ? CurrentBranchId.Value
        : DefaultBranchId ?? throw new InvalidOperationException("Chưa chọn chi nhánh làm việc.");

    /// <summary>Mã chi nhánh ghi dữ liệu (tiền tố số chứng từ)</summary>
    public async Task<string> WriteBranchCodeAsync()
    {
        if (_branchCode != null) return _branchCode;
        var id = WriteBranchId;
        return _branchCode = await Branches.AsNoTracking().Where(b => b.Id == id).Select(b => b.Code).FirstAsync();
    }

    // ----- Chi nhánh -----
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();

    // ----- Hệ thống -----
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<TranslationKey> TranslationKeys => Set<TranslationKey>();
    public DbSet<Translation> Translations => Set<Translation>();

    // ----- Khách hàng & thú cưng -----
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Species> Species => Set<Species>();
    public DbSet<Breed> Breeds => Set<Breed>();
    public DbSet<Pet> Pets => Set<Pet>();

    // ----- Dịch vụ & kho -----
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<StockReceiptLine> StockReceiptLines => Set<StockReceiptLine>();
    public DbSet<StockMove> StockMoves => Set<StockMove>();
    public DbSet<ProductStock> ProductStocks => Set<ProductStock>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();

    // ----- Khám chữa bệnh -----
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitService> VisitServices => Set<VisitService>();
    public DbSet<VisitMedicine> VisitMedicines => Set<VisitMedicine>();
    public DbSet<Vaccination> Vaccinations => Set<Vaccination>();
    public DbSet<Cage> Cages => Set<Cage>();
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<AdmissionNote> AdmissionNotes => Set<AdmissionNote>();

    // ----- Thu ngân -----
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();

    // ----- Báo cáo động -----
    public DbSet<DynamicReport> DynamicReports => Set<DynamicReport>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Tiền tệ 2 số lẻ; số lượng, cân nặng, nhiệt độ 3 số lẻ
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            p.SetPrecision(18);
            p.SetScale(p.Name is "Quantity" or "Stock" or "MinStock" or "Balance" or "WeightKg" or "Temperature" ? 3 : 2);
        }

        b.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();
        b.Entity<RolePermission>().HasIndex(x => new { x.Role, x.Permission }).IsUnique();
        b.Entity<AuditLog>().HasIndex(x => new { x.Entity, x.EntityId });
        b.Entity<AuditLog>().HasIndex(x => x.At);
        b.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Language>().HasIndex(x => x.Code).IsUnique();
        // Khóa dịch so sánh chính xác (phân biệt hoa / thường, dấu): "Đặt lịch" khác "đặt lịch"
        b.Entity<TranslationKey>().Property(x => x.Key).UseCollation("Latin1_General_100_BIN2");
        b.Entity<Translation>().Property(x => x.Key).UseCollation("Latin1_General_100_BIN2");
        b.Entity<TranslationKey>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Translation>().HasIndex(x => new { x.LanguageCode, x.Key }).IsUnique();

        b.Entity<Owner>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Owner>().HasIndex(x => x.Phone);
        b.Entity<Owner>().HasIndex(x => x.FullName);
        b.Entity<Pet>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Pet>().HasIndex(x => x.Name);
        b.Entity<Breed>().HasIndex(x => new { x.SpeciesId, x.Name }).IsUnique();

        b.Entity<ServiceItem>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Name);
        b.Entity<ProductBatch>().HasIndex(x => new { x.ProductId, x.ExpiryDate });
        b.Entity<StockReceipt>().HasIndex(x => x.Code).IsUnique();
        b.Entity<StockMove>().HasIndex(x => new { x.ProductId, x.At });
        b.Entity<StockMove>().HasIndex(x => new { x.RefType, x.RefId });

        b.Entity<Appointment>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Appointment>().HasIndex(x => x.StartAt);
        b.Entity<Visit>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Visit>().HasIndex(x => new { x.Status, x.VisitAt });
        b.Entity<Vaccination>().HasIndex(x => new { x.PetId, x.Date });
        b.Entity<Vaccination>().HasIndex(x => x.NextDueDate);
        b.Entity<Cage>().HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.Entity<Admission>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Admission>().HasIndex(x => x.Status);
        b.Entity<Invoice>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Invoice>().HasIndex(x => new { x.Status, x.Date });
        b.Entity<Payment>().HasIndex(x => x.At);

        // ----- Chi nhánh -----
        b.Entity<Branch>().HasIndex(x => x.Code).IsUnique();
        b.Entity<UserBranch>().HasIndex(x => new { x.UserId, x.BranchId }).IsUnique();
        b.Entity<AppUser>().HasMany(x => x.Branches).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        b.Entity<UserBranch>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<ProductStock>().HasIndex(x => new { x.BranchId, x.ProductId }).IsUnique();
        b.Entity<Product>().HasMany(x => x.Stocks).WithOne(x => x.Product).HasForeignKey(x => x.ProductId);
        b.Entity<StockTransfer>().HasIndex(x => x.Code).IsUnique();
        b.Entity<StockTransfer>().HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<StockTransfer>().HasOne(x => x.ToBranch).WithMany().HasForeignKey(x => x.ToBranchId);
        b.Entity<StockTransfer>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
        b.Entity<StockTransfer>().HasMany(x => x.Lines).WithOne(x => x.Transfer).HasForeignKey(x => x.TransferId);
        b.Entity<StockTransferLine>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<Owner>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Appointment>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Visit>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Vaccination>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Cage>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Admission>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<Invoice>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<StockReceipt>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<ProductBatch>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<StockMove>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.Entity<ProductStock>().HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);

        // ----- Bộ lọc dữ liệu theo chi nhánh đang làm việc (CurrentBranchId = null: không lọc) -----
        // Khách hàng: dùng chung toàn chuỗi hoặc riêng chi nhánh tạo (theo cài đặt); thú cưng theo chủ nuôi.
        b.Entity<Owner>().HasQueryFilter(x => CurrentBranchId == null || ShareCustomers || x.BranchId == CurrentBranchId);
        b.Entity<Pet>().HasQueryFilter(x => CurrentBranchId == null || ShareCustomers || x.Owner!.BranchId == CurrentBranchId);
        b.Entity<Appointment>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<Visit>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<VisitService>().HasQueryFilter(x => CurrentBranchId == null || x.Visit!.BranchId == CurrentBranchId);
        b.Entity<VisitMedicine>().HasQueryFilter(x => CurrentBranchId == null || x.Visit!.BranchId == CurrentBranchId);
        b.Entity<Vaccination>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<Cage>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<Admission>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<AdmissionNote>().HasQueryFilter(x => CurrentBranchId == null || x.Admission!.BranchId == CurrentBranchId);
        b.Entity<Invoice>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<InvoiceLine>().HasQueryFilter(x => CurrentBranchId == null || x.Invoice!.BranchId == CurrentBranchId);
        b.Entity<Payment>().HasQueryFilter(x => CurrentBranchId == null || x.Invoice!.BranchId == CurrentBranchId);
        b.Entity<StockReceipt>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<StockReceiptLine>().HasQueryFilter(x => CurrentBranchId == null || x.Receipt!.BranchId == CurrentBranchId);
        b.Entity<ProductBatch>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<StockMove>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<ProductStock>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId);
        b.Entity<StockTransfer>().HasQueryFilter(x => CurrentBranchId == null || x.BranchId == CurrentBranchId || x.ToBranchId == CurrentBranchId);
        b.Entity<StockTransferLine>().HasQueryFilter(x => CurrentBranchId == null || x.Transfer!.BranchId == CurrentBranchId || x.Transfer.ToBranchId == CurrentBranchId);

        // Quan hệ khai báo tường minh (tránh EF tự ghép cặp sai)
        b.Entity<Owner>().HasMany(x => x.Pets).WithOne(x => x.Owner).HasForeignKey(x => x.OwnerId);
        b.Entity<Species>().HasMany(x => x.Breeds).WithOne(x => x.Species).HasForeignKey(x => x.SpeciesId);
        b.Entity<Product>().HasMany(x => x.Batches).WithOne(x => x.Product).HasForeignKey(x => x.ProductId);
        b.Entity<StockReceipt>().HasMany(x => x.Lines).WithOne(x => x.Receipt).HasForeignKey(x => x.ReceiptId);
        b.Entity<Visit>().HasMany(x => x.Services).WithOne(x => x.Visit).HasForeignKey(x => x.VisitId);
        b.Entity<Visit>().HasMany(x => x.Medicines).WithOne(x => x.Visit).HasForeignKey(x => x.VisitId);
        b.Entity<Admission>().HasMany(x => x.Notes).WithOne(x => x.Admission).HasForeignKey(x => x.AdmissionId);
        b.Entity<Invoice>().HasMany(x => x.Lines).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId);
        b.Entity<Invoice>().HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId);
        b.Entity<Pet>().HasOne(x => x.Species).WithMany().HasForeignKey(x => x.SpeciesId);
        b.Entity<Pet>().HasOne(x => x.Breed).WithMany().HasForeignKey(x => x.BreedId);
        b.Entity<StockReceipt>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
        b.Entity<StockReceiptLine>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<StockMove>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<StockMove>().HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId);
        b.Entity<StockMove>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Appointment>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
        b.Entity<Appointment>().HasOne(x => x.Pet).WithMany().HasForeignKey(x => x.PetId);
        b.Entity<Appointment>().HasOne(x => x.Vet).WithMany().HasForeignKey(x => x.VetId);
        b.Entity<Appointment>().HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId);
        b.Entity<Visit>().HasOne(x => x.Pet).WithMany().HasForeignKey(x => x.PetId);
        b.Entity<Visit>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
        b.Entity<Visit>().HasOne(x => x.Vet).WithMany().HasForeignKey(x => x.VetId);
        b.Entity<VisitService>().HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId);
        b.Entity<VisitMedicine>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<Vaccination>().HasOne(x => x.Pet).WithMany().HasForeignKey(x => x.PetId);
        b.Entity<Vaccination>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<Vaccination>().HasOne(x => x.Vet).WithMany().HasForeignKey(x => x.VetId);
        b.Entity<Vaccination>().HasOne(x => x.Visit).WithMany().HasForeignKey(x => x.VisitId);
        b.Entity<Admission>().HasOne(x => x.Pet).WithMany().HasForeignKey(x => x.PetId);
        b.Entity<Admission>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
        b.Entity<Admission>().HasOne(x => x.Cage).WithMany().HasForeignKey(x => x.CageId);
        b.Entity<Admission>().HasOne(x => x.Vet).WithMany().HasForeignKey(x => x.VetId);
        b.Entity<AdmissionNote>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Invoice>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
        b.Entity<Invoice>().HasOne(x => x.Pet).WithMany().HasForeignKey(x => x.PetId);
        b.Entity<Invoice>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById);
        b.Entity<InvoiceLine>().HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId);
        b.Entity<InvoiceLine>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
        b.Entity<InvoiceLine>().HasOne(x => x.Vet).WithMany().HasForeignKey(x => x.VetId);
        b.Entity<Payment>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        // Chỉ dòng chi tiết được xóa theo chứng từ cha; mọi khóa ngoại khác ON DELETE NO ACTION
        var cascadeAllowed = new HashSet<(Type Child, Type Parent)>
        {
            (typeof(StockReceiptLine), typeof(StockReceipt)), (typeof(VisitService), typeof(Visit)),
            (typeof(VisitMedicine), typeof(Visit)), (typeof(AdmissionNote), typeof(Admission)),
            (typeof(InvoiceLine), typeof(Invoice)), (typeof(StockTransferLine), typeof(StockTransfer)), (typeof(UserBranch), typeof(AppUser))
        };
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = cascadeAllowed.Contains((fk.DeclaringEntityType.ClrType, fk.PrincipalEntityType.ClrType))
                ? DeleteBehavior.Cascade : DeleteBehavior.NoAction;
    }

    // ================= Nhật ký hệ thống tự động =================

    /// <summary>Tạm tắt nhật ký từng bản ghi (nạp dữ liệu mô phỏng hàng loạt)</summary>
    public bool SuppressAudit { get; set; }

    private static readonly HashSet<Type> NotAudited = new() { typeof(AuditLog), typeof(StockMove), typeof(ProductBatch) };
    private static readonly HashSet<string> NoiseProps = new() { "LastLoginAt", "Stock", "Paid", "Balance" };
    private static readonly HashSet<string> SecretProps = new() { "PasswordHash" };

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignBranch();
        // Chỉ ghi nhật ký cho thao tác của người dùng (bỏ qua khởi tạo dữ liệu nền)
        if (_writingAudit || SuppressAudit || _http.HttpContext?.User.Identity?.IsAuthenticated != true)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        var pending = CollectAudit();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (pending.Count == 0) return result;

        var (uid, uname, ip) = CurrentActor();
        foreach (var p in pending)
        {
            var key = p.Entry.State == EntityState.Detached ? p.KeyBefore : KeyOf(p.Entry);
            AuditLogs.Add(new AuditLog
            {
                At = DateTime.Now, UserId = uid, UserName = uname, IpAddress = ip,
                Entity = p.Entry.Metadata.ClrType.Name, EntityId = key, Action = p.Action,
                Changes = p.Changes.Count == 0 ? null : JsonSerializer.Serialize(p.Changes, JsonOpts)
            });
        }
        _writingAudit = true;
        try { await base.SaveChangesAsync(true, cancellationToken); }
        finally { _writingAudit = false; }
        return result;
    }

    /// <summary>Bản ghi mới thuộc chi nhánh đang làm việc (nếu chưa gán)</summary>
    private void AssignBranch()
    {
        foreach (var e in ChangeTracker.Entries<IBranchScoped>())
            if (e.State == EntityState.Added && e.Entity.BranchId == 0) e.Entity.BranchId = WriteBranchId;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record PendingAudit(EntityEntry Entry, string Action, string? KeyBefore, Dictionary<string, object?> Changes);

    private List<PendingAudit> CollectAudit()
    {
        ChangeTracker.DetectChanges();
        var list = new List<PendingAudit>();
        foreach (var e in ChangeTracker.Entries())
        {
            if (NotAudited.Contains(e.Metadata.ClrType)) continue;
            switch (e.State)
            {
                case EntityState.Added:
                {
                    var changes = new Dictionary<string, object?>();
                    foreach (var p in e.Properties)
                    {
                        if (p.Metadata.IsPrimaryKey() || p.CurrentValue == null) continue;
                        changes[p.Metadata.Name] = Mask(p.Metadata.Name, p.CurrentValue);
                    }
                    list.Add(new PendingAudit(e, "Tạo", null, changes));
                    break;
                }
                case EntityState.Modified:
                {
                    var changes = new Dictionary<string, object?>();
                    foreach (var p in e.Properties)
                    {
                        if (!p.IsModified || NoiseProps.Contains(p.Metadata.Name)) continue;
                        if (Equals(p.OriginalValue, p.CurrentValue)) continue;
                        changes[p.Metadata.Name] = new[] { Mask(p.Metadata.Name, p.OriginalValue), Mask(p.Metadata.Name, p.CurrentValue) };
                    }
                    if (changes.Count == 0) continue;
                    list.Add(new PendingAudit(e, "Sửa", null, changes));
                    break;
                }
                case EntityState.Deleted:
                    list.Add(new PendingAudit(e, "Xóa", KeyOf(e), new Dictionary<string, object?>()));
                    break;
            }
        }
        return list;
    }

    private static object? Mask(string prop, object? value) =>
        value == null ? null : SecretProps.Contains(prop) ? "***" : value is Enum en ? en.ToString() : value;

    private static string? KeyOf(EntityEntry e)
    {
        var pk = e.Metadata.FindPrimaryKey();
        if (pk == null) return null;
        return string.Join(",", pk.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString()));
    }

    private (int? Id, string? Name, string? Ip) CurrentActor()
    {
        var ctx = _http.HttpContext;
        var user = ctx?.User;
        if (user?.Identity?.IsAuthenticated != true) return (null, "Hệ thống", null);
        int? id = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : null;
        var name = user.FindFirstValue("FullName") ?? user.Identity?.Name;
        return (id, name, ctx?.Connection.RemoteIpAddress?.ToString());
    }
}
