using System.Security.Claims;
using System.Text.Json;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace PetHospital.Web.Data;

public class PetDbContext : DbContext
{
    private readonly IHttpContextAccessor _http;
    private bool _writingAudit;

    public PetDbContext(DbContextOptions<PetDbContext> options, IHttpContextAccessor http) : base(options) => _http = http;

    // ----- Hệ thống -----
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

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
        b.Entity<Cage>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Admission>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Admission>().HasIndex(x => x.Status);
        b.Entity<Invoice>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Invoice>().HasIndex(x => new { x.Status, x.Date });
        b.Entity<Payment>().HasIndex(x => x.At);

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
            (typeof(InvoiceLine), typeof(Invoice))
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
