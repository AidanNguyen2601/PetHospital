using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Data;

public record FlowSimResult(int Days, int NewOwners, int NewPets, int Appointments, int NoShows, int Visits, int Vaccinations,
    int Admissions, int Receipts, int Invoices, int Payments, int Cancelled, decimal Revenue);

/// <summary>
/// Mô phỏng luồng nghiệp vụ phòng khám theo từng ngày, NẠP THÊM vào dữ liệu đang có (không xóa gì):
/// đặt lịch → khách đến / không đến → phiếu khám (sinh hiệu, chẩn đoán, dịch vụ, đơn thuốc, tiêm phòng) → hẹn tái khám / mũi nhắc lại
/// → nằm viện hoặc lưu chuồng (theo dõi hằng ngày, xuất chuồng) → hóa đơn phát hành (trừ kho FEFO, giá vốn) → thu tiền ngay / trả sau / còn nợ
/// → kho tự nhập bổ sung khi xuống dưới tồn tối thiểu. Ngày hôm nay có hàng chờ khám, hóa đơn nháp chờ thu ngân; 7 ngày tới có lịch hẹn.
/// Dùng danh mục hiện có (dịch vụ, thuốc, chuồng, bác sĩ); chạy trong một transaction.
/// </summary>
public static class FlowSimulator
{
    public static readonly int[] DayOptions = { 7, 14, 30, 60, 90 };
    public static readonly (int Code, string Name, int Min, int Max)[] Levels =
    {
        (1, "Vắng khách (3–5 lượt/ngày)", 3, 5),
        (2, "Bình thường (6–9 lượt/ngày)", 6, 9),
        (3, "Đông khách (10–14 lượt/ngày)", 10, 14)
    };

    public static async Task<FlowSimResult> RunAsync(PetDbContext db, StockService stock, int days, int level = 2, bool includeToday = true, int? seed = null)
    {
        days = Math.Clamp(days, 1, 180);
        var lv = Levels.FirstOrDefault(l => l.Code == level);
        if (lv.Code == 0) lv = Levels[1];
        var sim = new Sim(db, stock, new Random(seed ?? Environment.TickCount), lv.Min, lv.Max);
        var oldAudit = db.SuppressAudit;
        db.SuppressAudit = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var r = await sim.RunAsync(days, includeToday);
            await tx.CommitAsync();
            return r;
        }
        finally
        {
            db.SuppressAudit = oldAudit;
            db.ChangeTracker.Clear();
        }
    }

    // ===================================================================================================
    private sealed class PetInfo
    {
        public int Id, OwnerId, SpeciesId;
        public string Name = "", OwnerName = "";
        public bool IsDog, IsCat, Neutered;
        public PetGender Gender;
        public decimal Weight;
        public DateTime? BirthDate;
        public bool Young(DateTime d) => BirthDate.HasValue && BirthDate.Value > d.AddMonths(-5);
    }

    private enum PlanKind { Exam, Recheck, Vaccine, Surgery, Grooming }

    private sealed record Plan(PlanKind Kind, PetInfo Pet, int? VetId, string Reason, int? VaccineId = null, int Dose = 1, string? Diagnosis = null);

    private sealed record Scenario(int Species, string Reason, string Symptoms, string Exam, string Diagnosis, string Treatment, string Advice,
        string[] Services, (string Code, decimal Qty, string Dose, int Days)[] Meds, int RecheckDays = 0, bool Severe = false);

    private static readonly Scenario[] Scenarios =
    {
        new(0, "Bỏ ăn, nôn ói", "Nôn 2–3 lần từ hôm qua, bỏ ăn, phân lỏng", "Niêm mạc hơi nhợt, bụng chướng nhẹ, mất nước ~5%", "Viêm dạ dày – ruột cấp",
            "Truyền dịch bù nước, kháng sinh, men tiêu hóa; ăn cháo loãng 2 ngày", "Cho ăn ít một, chia nhiều bữa; tái khám nếu còn nôn",
            new[] { "KB01", "XN03", "DT01" }, new[] { ("TH03", 10m, "Uống 1/2 viên x 2 lần/ngày", 5), ("TH12", 6m, "1 gói x 2 lần/ngày", 3), ("TH13", 1m, "Truyền tĩnh mạch", 1) }, 5),
        new(1, "Nôn ra máu, tiêu chảy có máu", "Bỏ ăn hoàn toàn, li bì, tiêu chảy mùi tanh", "Sốt 40.1°C, mất nước ~8%, test Parvo dương tính", "Viêm ruột do Parvovirus",
            "Nhập viện cách ly, truyền dịch, kháng sinh, chống nôn", "Cách ly với chó khác, khử trùng nơi ở",
            new[] { "KB01", "XN03", "XN01", "DT01" }, new[] { ("TH13", 2m, "Truyền tĩnh mạch", 1), ("TH14", 4m, "Tiêm dưới da", 1) }, 0, true),
        new(0, "Ngứa, rụng lông", "Gãi nhiều vùng lưng và tai, rụng lông từng mảng", "Da đỏ, có vảy gàu, soi thấy nấm và ve", "Viêm da do nấm kết hợp ký sinh trùng",
            "Tắm thuốc 2 lần/tuần, trị ký sinh trùng ngoài da", "Giặt ổ nằm, tránh tắm xà phòng thường",
            new[] { "KB01", "XN04" }, new[] { ("TH05", 10m, "Uống 1/2 viên/ngày", 5), ("TH08", 1m, "Uống 1 viên", 1) }, 14),
        new(0, "Lắc đầu, gãi tai", "Tai có mùi hôi, dịch nâu", "Ống tai đỏ, nhiều ráy tai", "Viêm tai ngoài",
            "Vệ sinh tai, nhỏ thuốc 7 ngày", "Giữ tai khô, không cho nước vào tai khi tắm",
            new[] { "KB01", "DT03" }, new[] { ("TH06", 1m, "Nhỏ 4 giọt x 2 lần/ngày", 7) }, 7),
        new(0, "Ho, sổ mũi", "Ho khan nhiều về đêm, chảy nước mũi trong", "Nhiệt độ 39.4°C, phổi có tiếng rít nhẹ", "Viêm đường hô hấp trên",
            "Kháng sinh 7 ngày, giữ ấm", "Tránh gió lạnh, cho uống nước ấm",
            new[] { "KB01", "DT02" }, new[] { ("TH01", 14m, "Uống 1 viên x 2 lần/ngày", 7), ("TH14", 2m, "Tiêm dưới da", 1) }, 7),
        new(1, "Đi khập khiễng", "Chân sau đi cà nhắc sau khi chạy nhảy", "Đau khi gập khớp gối, không sưng", "Bong gân khớp gối",
            "Hạn chế vận động 2 tuần, giảm đau", "Không cho leo cầu thang, chạy nhảy",
            new[] { "KB01", "HA02" }, new[] { ("TH04", 1m, "Uống 0.1ml/kg/ngày", 7) }, 14),
        new(0, "Khám sức khỏe định kỳ", "Không có biểu hiện bất thường", "Thể trạng tốt, răng có cao răng nhẹ", "Sức khỏe bình thường",
            "Tẩy giun định kỳ, theo dõi cân nặng", "Tẩy giun 3 tháng/lần",
            new[] { "KB01", "XN01" }, new[] { ("TH10", 1m, "Uống 1 viên", 1) }),
        new(0, "Tiểu khó, tiểu ra máu", "Đi tiểu nhiều lần, ít nước tiểu, có máu", "Bàng quang căng, đau khi sờ", "Viêm bàng quang",
            "Kháng sinh, uống nhiều nước, thức ăn hỗ trợ tiết niệu", "Cho uống nhiều nước, theo dõi lượng nước tiểu",
            new[] { "KB01", "HA01", "XN05" }, new[] { ("TH02", 14m, "Uống 1 viên x 2 lần/ngày", 7) }, 7),
        new(1, "Bị chó khác cắn", "Vết thương vùng cổ, chảy máu", "Vết rách 2cm, sạch, không tổn thương sâu", "Vết thương phần mềm",
            "Khâu vết thương, kháng sinh, đeo loa chống liếm", "Thay băng hằng ngày, cắt chỉ sau 7–10 ngày",
            new[] { "KB01", "PT06" }, new[] { ("TH01", 10m, "Uống 1 viên x 2 lần/ngày", 5), ("VT04", 1m, "Đeo 7 ngày", 7), ("VT03", 2m, "Thay băng hằng ngày", 3) }, 7),
        new(2, "Hắt hơi, chảy nước mắt", "Hắt hơi liên tục, mắt kèm nhèm, giảm ăn", "Viêm kết mạc 2 bên, loét nhẹ đầu lưỡi", "Hội chứng hô hấp trên ở mèo (nghi Calici / Herpes)",
            "Kháng sinh, bổ sung dinh dưỡng, vệ sinh mắt mũi", "Lau mắt bằng nước muối sinh lý 3 lần/ngày",
            new[] { "KB01", "DT02" }, new[] { ("TH01", 10m, "Uống 1/2 viên x 2 lần/ngày", 5), ("TH15", 1m, "Bôi miệng 2g/ngày", 7) }, 7),
        new(2, "Nôn ra búi lông, táo bón", "Nôn dịch kèm lông, 3 ngày chưa đi phân", "Bụng cứng nhẹ, siêu âm thấy phân ứ đọng", "Búi lông dạ dày – táo bón",
            "Gel tiêu búi lông, bổ sung chất xơ", "Chải lông hằng ngày, cho uống nhiều nước",
            new[] { "KB01", "HA01" }, new[] { ("TH15", 1m, "Bôi miệng 2g x 2 lần/ngày", 7), ("TH12", 6m, "1 gói/ngày", 6) }, 10),
        new(2, "Tiểu khó, liếm vùng sinh dục", "Ngồi tiểu lâu, kêu khi tiểu", "Bàng quang căng to, đau", "Bệnh đường tiết niệu dưới ở mèo (FLUTD)",
            "Đặt thông tiểu, nhập viện theo dõi, truyền dịch", "Đổi thức ăn hạt hỗ trợ tiết niệu",
            new[] { "KB01", "HA01", "XN05", "DT01" }, new[] { ("TH13", 1m, "Truyền tĩnh mạch", 1), ("TH02", 10m, "Uống 1/2 viên x 2 lần/ngày", 5) }, 0, true),
        new(0, "Có ve, bọ chét", "Gãi nhiều, thấy ve bám ở tai và kẽ chân", "Nhiều ve, da mẩn đỏ nhẹ", "Nhiễm ve – bọ chét",
            "Nhổ ve, dùng thuốc trị ký sinh trùng ngoài da", "Vệ sinh môi trường, xịt ve nơi ở",
            new[] { "KB01" }, new[] { ("TH07", 1m, "Uống 1 viên / 12 tuần", 1) })
    };

    // ===================================================================================================
    private sealed class Sim
    {
        private readonly PetDbContext _db;
        private readonly StockService _stock;
        private readonly Random _r;
        private readonly int _min, _max;
        private readonly DateTime _today = DateTime.Today, _now = DateTime.Now;

        private List<int> _vets = new();
        private int _reception, _cashier, _storekeeper;
        private Dictionary<string, ServiceItem> _svc = new();
        private ServiceItem _exam = null!;
        private Dictionary<string, (int Id, string Name, string Unit, decimal Price, ProductKind Kind, int? Booster, bool Track, decimal Min)> _prd = new();
        private List<(int Id, string Code, string Size, bool Iso, decimal Rate)> _cages = new();
        private readonly Dictionary<int, DateTime> _cageBusyUntil = new();
        private int? _dogSpecies, _catSpecies;
        private Dictionary<int, List<int>> _breeds = new();
        private List<PetInfo> _pets = new();
        private int _ownerSeq, _petSeq;
        private readonly Dictionary<string, int> _seq = new();
        private readonly Dictionary<(int Pet, int Product), int> _doses = new();
        private readonly Dictionary<DateTime, List<Plan>> _plans = new();
        private readonly Dictionary<DateTime, List<(int InvoiceId, PaymentMethod Method)>> _laterPays = new();
        private (int Id, string Code)? _dogCore, _rabies, _catCore;
        private Dictionary<int, string> _codeById = new();
        private HashSet<string> _phones = new();

        private int _newOwners, _newPets, _appts, _noShows, _visits, _vaccinations, _admissions, _receipts, _invoices, _payments, _cancelled;
        private decimal _revenue;

        public Sim(PetDbContext db, StockService stock, Random r, int min, int max) { _db = db; _stock = stock; _r = r; _min = min; _max = max; }

        // ---------------------------------------------------------------------------------------------
        public async Task<FlowSimResult> RunAsync(int days, bool includeToday)
        {
            await LoadAsync();
            var start = _today.AddDays(-days);
            var end = includeToday ? _today : _today.AddDays(-1);
            await PlanDueVaccinesAsync(start, end);

            for (var d = start; d <= end; d = d.AddDays(1))
            {
                await RestockAsync(d, d == start || d.DayOfWeek == DayOfWeek.Monday);
                await SimulateDayAsync(d);
                await CollectLaterPaymentsAsync(d);
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }

            await BookUpcomingAsync(end);
            await RebuildStockBalancesAsync();
            return new FlowSimResult(days, _newOwners, _newPets, _appts, _noShows, _visits, _vaccinations, _admissions, _receipts, _invoices, _payments, _cancelled, _revenue);
        }

        // ---------------------------------------------------------------------------------------------
        private async Task LoadAsync()
        {
            var simBranch = _db.WriteBranchId;
            var users = await _db.Users.AsNoTracking().Where(u => u.IsActive && (u.AllBranches || u.Branches.Any(b => b.BranchId == simBranch))).ToListAsync();
            _vets = users.Where(u => u.IsVet).Select(u => u.Id).ToList();
            if (_vets.Count == 0) throw new InvalidOperationException("Chưa có bác sĩ thú y (người dùng có cờ bác sĩ). Hãy tạo ít nhất một bác sĩ trước khi mô phỏng.");
            int Pick(string role) => users.FirstOrDefault(u => u.RoleList().Contains(role))?.Id ?? users.FirstOrDefault(u => u.RoleList().Contains(AppRoles.Admin))?.Id ?? users[0].Id;
            _reception = Pick(AppRoles.Receptionist);
            _cashier = Pick(AppRoles.Cashier);
            _storekeeper = Pick(AppRoles.Warehouse);

            var services = await _db.ServiceItems.AsNoTracking().Where(s => s.IsActive).ToListAsync();
            _svc = services.GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First());
            _exam = _svc.GetValueOrDefault("KB01") ?? services.FirstOrDefault(s => s.Kind == ServiceKind.Exam)
                ?? throw new InvalidOperationException("Chưa có dịch vụ khám bệnh trong danh mục Dịch vụ & bảng giá.");
            // Mã dịch vụ khác nhau giữa các phòng khám: map mã chuẩn sang dịch vụ cùng nhóm nếu thiếu
            void Fallback(string code, ServiceKind kind) { if (!_svc.ContainsKey(code) && services.FirstOrDefault(s => s.Kind == kind) is { } s) _svc[code] = s; }
            Fallback("KB02", ServiceKind.Exam); Fallback("TP01", ServiceKind.Vaccination); Fallback("XN01", ServiceKind.Lab);
            Fallback("HA01", ServiceKind.Imaging); Fallback("DT01", ServiceKind.Treatment); Fallback("SP01", ServiceKind.Grooming);

            var products = await _db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
            _prd = products.GroupBy(p => p.Code).ToDictionary(g => g.Key, g => { var p = g.First(); return (p.Id, p.Name, p.Unit, p.SalePrice, p.Kind, p.BoosterDays, p.TrackExpiry, p.MinStock); });
            _codeById = _prd.ToDictionary(x => x.Value.Id, x => x.Key);
            _phones = new HashSet<string>(await _db.Owners.IgnoreQueryFilters().Select(o => o.Phone).ToListAsync());
            var vaccines = products.Where(p => p.Kind == ProductKind.Vaccine).ToList();
            (int, string)? V(string code, string contains) =>
                (vaccines.FirstOrDefault(p => p.Code == code) ?? vaccines.FirstOrDefault(p => p.Name.Contains(contains, StringComparison.OrdinalIgnoreCase))) is { } p ? (p.Id, p.Code) : null;
            _dogCore = V("VX01", "chó"); _rabies = V("VX02", "Dại"); _catCore = V("VX03", "mèo");

            _cages = (await _db.Cages.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync())
                .Select(c => (c.Id, c.Code, c.Size ?? "", c.IsIsolation, c.DailyRate)).ToList();
            foreach (var id in await _db.Admissions.AsNoTracking().Where(a => a.Status == AdmissionStatus.Active && a.CageId != null).Select(a => a.CageId!.Value).ToListAsync())
                _cageBusyUntil[id] = DateTime.MaxValue;

            var species = await _db.Species.AsNoTracking().Include(s => s.Breeds).ToListAsync();
            _dogSpecies = species.FirstOrDefault(s => s.Name == "Chó")?.Id;
            _catSpecies = species.FirstOrDefault(s => s.Name == "Mèo")?.Id;
            _breeds = species.ToDictionary(s => s.Id, s => s.Breeds.Select(b => b.Id).ToList());

            _pets = (await _db.Pets.AsNoTracking().Include(p => p.Owner).Where(p => !p.IsDeceased && p.Owner!.IsActive).ToListAsync())
                .Select(p => new PetInfo
                {
                    Id = p.Id, OwnerId = p.OwnerId, OwnerName = p.Owner!.FullName, Name = p.Name, SpeciesId = p.SpeciesId ?? 0,
                    IsDog = p.SpeciesId == _dogSpecies, IsCat = p.SpeciesId == _catSpecies, Neutered = p.Neutered, Gender = p.Gender,
                    Weight = p.WeightKg ?? (p.SpeciesId == _catSpecies ? 4 : 8), BirthDate = p.BirthDate
                }).ToList();

            _ownerSeq = MaxNumber(await _db.Owners.IgnoreQueryFilters().Select(o => o.Code).ToListAsync(), "KH");
            _petSeq = MaxNumber(await _db.Pets.IgnoreQueryFilters().Select(o => o.Code).ToListAsync(), "TC");

            foreach (var g in await _db.Vaccinations.AsNoTracking().Where(v => v.ProductId != null)
                         .GroupBy(v => new { v.PetId, v.ProductId }).Select(g => new { g.Key.PetId, g.Key.ProductId, Max = g.Max(x => x.DoseNumber) }).ToListAsync())
                _doses[(g.PetId, g.ProductId!.Value)] = g.Max;
        }

        private static int MaxNumber(IEnumerable<string> codes, string prefix) =>
            codes.Where(c => c.StartsWith(prefix)).Select(c => int.TryParse(c[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();

        /// <summary>Số chứng từ: TIỀN TỐ + yyMM + "-" + STT, nối tiếp số lớn nhất đang có trong tháng</summary>
        private async Task<string> CodeAsync(string prefix, DateTime at)
        {
            var head = await Codes.HeadAsync(_db, prefix, at);
            if (!_seq.TryGetValue(head, out var n))
            {
                IQueryable<string> q = prefix switch
                {
                    "PK" => _db.Visits.Select(x => x.Code), "HD" => _db.Invoices.Select(x => x.Code), "LH" => _db.Appointments.Select(x => x.Code),
                    "NT" => _db.Admissions.Select(x => x.Code), _ => _db.StockReceipts.Select(x => x.Code)
                };
                var last = await q.IgnoreQueryFilters().Where(c => c.StartsWith(head)).OrderByDescending(c => c).FirstOrDefaultAsync();
                n = last != null && int.TryParse(last[head.Length..], out var v) ? v : 0;
            }
            _seq[head] = ++n;
            return head + n.ToString("0000");
        }

        private DateTime Cap(DateTime at) => at > _now ? _now : at;
        private T Pick<T>(IReadOnlyList<T> list) => list[_r.Next(list.Count)];
        private bool Chance(double p) => _r.NextDouble() < p;
        private void AddPlan(DateTime date, Plan p) { if (!_plans.TryGetValue(date.Date, out var l)) _plans[date.Date] = l = new(); l.Add(p); }

        private PaymentMethod RandomMethod()
        {
            var x = _r.Next(100);
            return x < 45 ? PaymentMethod.Cash : x < 80 ? PaymentMethod.Transfer : x < 92 ? PaymentMethod.Card : PaymentMethod.EWallet;
        }

        /// <summary>Gán thời điểm thực tế cho các dòng thẻ kho vừa sinh (dịch vụ kho ghi DateTime.Now)</summary>
        private void StampMoves(DateTime at)
        {
            foreach (var e in _db.ChangeTracker.Entries<StockMove>().Where(e => e.State == EntityState.Added)) e.Entity.At = at;
        }

        // ---------------------------------------------------------------------------------------------
        /// <summary>Thú cưng có mũi nhắc lại đến hạn trong khoảng mô phỏng → phần lớn khách đặt lịch tiêm đúng hẹn</summary>
        private async Task PlanDueVaccinesAsync(DateTime start, DateTime end)
        {
            var due = await _db.Vaccinations.AsNoTracking()
                .Where(v => !v.ReminderDone && v.ProductId != null && v.NextDueDate >= start && v.NextDueDate <= end.AddDays(7))
                .Select(v => new { v.PetId, ProductId = v.ProductId!.Value, v.DoseNumber, Due = v.NextDueDate!.Value, v.VetId }).ToListAsync();
            var pets = _pets.ToDictionary(p => p.Id);
            foreach (var v in due)
            {
                if (!pets.TryGetValue(v.PetId, out var pet) || !Chance(0.75)) continue;
                var date = v.Due.AddDays(_r.Next(-1, 4));
                if (date < start) date = start;
                AddPlan(date, new Plan(PlanKind.Vaccine, pet, v.VetId, $"Tiêm nhắc lại mũi {v.DoseNumber + 1}", v.ProductId, v.DoseNumber + 1));
            }
        }

        // ---------------------------------------------------------------------------------------------
        /// <summary>Nhập bổ sung định kỳ (thứ Hai) các mặt hàng dưới 2 lần tồn tối thiểu</summary>
        private async Task RestockAsync(DateTime d, bool scheduled)
        {
            if (!scheduled) return;
            var low = await _db.Products.Include(p => p.Stocks).Where(p => p.IsActive && p.MinStock > 0 && p.Stocks.Sum(s => s.Quantity) < p.MinStock * 2).ToListAsync();
            if (low.Count == 0) return;
            var lines = low.Select(p => (p, Math.Max(p.MinStock * 5 - p.Stock, p.MinStock))).ToList();
            await ReceiveAsync(d.AddHours(7.5), "Công ty TNHH Dược Thú y Sài Gòn", "Nhập bổ sung định kỳ", lines);
        }

        /// <summary>Nhập gấp khi phát hành hóa đơn mà tồn không đủ</summary>
        private async Task EnsureStockAsync(DateTime at, IEnumerable<InvoiceLine> lines)
        {
            var need = lines.Where(l => l.ProductId.HasValue).GroupBy(l => l.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            if (need.Count == 0) return;
            var ids = need.Keys.ToList();
            var short_ = (await _db.Products.Include(p => p.Stocks).Where(p => ids.Contains(p.Id)).ToListAsync()).Where(p => p.Stock < need[p.Id]).ToList();
            if (short_.Count == 0) return;
            await ReceiveAsync(at.AddMinutes(-30), "Nhà thuốc thú y Phương Nam", "Nhập gấp do thiếu hàng",
                short_.Select(p => (p, need[p.Id] - p.Stock + Math.Max(p.MinStock * 3, 5))).ToList());
        }

        private async Task ReceiveAsync(DateTime at, string supplier, string note, List<(Product P, decimal Qty)> items)
        {
            var r = new StockReceipt
            {
                Code = await CodeAsync("NK", at), Date = at.Date, Supplier = supplier, SupplierInvoiceNo = "HĐ" + _r.Next(100000, 999999), Note = note,
                CreatedById = _storekeeper, CreatedAt = at
            };
            foreach (var (p, qty) in items)
            {
                var q = Math.Ceiling(qty);
                // Giá nhập dao động ±5% quanh giá vốn hiện tại
                var cost = Math.Round(p.CostPrice * (decimal)(0.95 + _r.NextDouble() * 0.1) / 100m, 0) * 100m;
                if (cost <= 0) cost = Math.Round(p.SalePrice * 0.6m, 0);
                r.Lines.Add(new StockReceiptLine
                {
                    ProductId = p.Id, Quantity = q, UnitCost = cost, Amount = q * cost,
                    LotNo = p.TrackExpiry ? $"L{at:yyMM}{_r.Next(100, 999)}" : null,
                    ExpiryDate = p.TrackExpiry ? at.Date.AddMonths(p.Kind == ProductKind.Vaccine ? _r.Next(8, 15) : _r.Next(12, 30)) : null
                });
            }
            r.Total = r.Lines.Sum(l => l.Amount);
            _db.StockReceipts.Add(r);
            await _db.SaveChangesAsync();
            await _stock.PostReceiptAsync(r);
            r.PostedAt = at;
            StampMoves(at);
            await _db.SaveChangesAsync();
            _receipts++;
        }

        // ---------------------------------------------------------------------------------------------
        private async Task SimulateDayAsync(DateTime d)
        {
            var isToday = d == _today;
            var target = d.DayOfWeek == DayOfWeek.Sunday ? Math.Max(2, _min / 2) : _r.Next(_min, _max + 1);
            var plans = _plans.GetValueOrDefault(d) ?? new();
            _plans.Remove(d);

            // Lượt ngẫu nhiên: bệnh, tiêm phòng, spa, phẫu thuật theo lịch
            while (plans.Count < target)
            {
                var pet = Chance(0.12) || _pets.Count == 0 ? await NewCustomerAsync(d) : Pick(_pets);
                var x = _r.Next(100);
                if (x < 55) plans.Add(new Plan(PlanKind.Exam, pet, null, ""));
                else if (x < 72 && (pet.IsDog || pet.IsCat)) plans.Add(new Plan(PlanKind.Vaccine, pet, null, pet.Young(d) ? "Tiêm phòng mũi 1" : "Tiêm phòng nhắc lại hằng năm"));
                else if (x < 92) plans.Add(new Plan(PlanKind.Grooming, pet, null, "Tắm, vệ sinh"));
                else if (!pet.Neutered && (pet.IsDog || pet.IsCat) && !pet.Young(d)) plans.Add(new Plan(PlanKind.Surgery, pet, null, "Triệt sản theo yêu cầu"));
                else plans.Add(new Plan(PlanKind.Exam, pet, null, ""));
            }

            var slot = 8.0;
            var step = 11.0 / Math.Max(plans.Count, 1);
            foreach (var p in plans.OrderBy(_ => _r.Next()))
            {
                var at = d.AddHours(slot).AddMinutes(_r.Next(0, 4) * 5);
                slot += step;
                var vet = p.VetId is { } v && _vets.Contains(v) ? v : Pick(_vets);
                // Lượt có hẹn trước: tái khám, tiêm nhắc, phẫu thuật luôn có lịch; còn lại 60% đặt lịch, 40% vãng lai
                var booked = p.Kind is PlanKind.Recheck or PlanKind.Surgery || p.VetId.HasValue || Chance(0.6);
                await EncounterAsync(d, at, p, vet, booked, isToday);
            }

            // Lưu chuồng gửi giữ (khoảng 1 lượt / 3 ngày)
            if (Chance(0.33) && _pets.Count > 0) await BoardingAsync(d.AddHours(_r.Next(8, 18)), Pick(_pets));
            // Khách lẻ mua thức ăn / phụ kiện
            for (var i = _r.Next(0, 3); i > 0; i--)
            {
                var at = d.AddHours(_r.Next(9, 20)).AddMinutes(_r.Next(0, 12) * 5);
                if (isToday && at > _now) continue;
                await RetailAsync(at);
            }
        }

        private async Task<PetInfo> NewCustomerAsync(DateTime d)
        {
            string[] last = { "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Trương", "Mai" };
            string[] mid = { "Thị", "Văn", "Minh", "Ngọc", "Thanh", "Hoàng", "Quốc", "Gia", "Bảo", "Thu", "Hữu", "Khánh" };
            string[] first = { "Anh", "Bích", "Cường", "Dung", "Giang", "Hà", "Hải", "Khoa", "Linh", "Long", "My", "Nam", "Nhung", "Phong", "Quân", "Tâm", "Thảo", "Trang", "Tuấn", "Vy", "Yến", "Duy", "Hạnh", "Phúc", "Uyên" };
            string[] streets = { "Nguyễn Thị Thập", "Huỳnh Tấn Phát", "Lê Văn Lương", "Nguyễn Hữu Thọ", "Phạm Hùng", "Tôn Dật Tiên", "Nguyễn Lương Bằng", "Trần Xuân Soạn", "Lâm Văn Bền", "Nguyễn Văn Linh" };
            string[] dogNames = { "Lu", "Milo", "Bơ", "Mực", "Vàng", "Cún", "Bông", "Lucky", "Rocky", "Bin", "Ki", "Mochi", "Sữa", "Đậu", "Tôm", "Max", "Coco", "Na" };
            string[] catNames = { "Mướp", "Miu", "Mimi", "Tom", "Kem", "Gấu", "Su", "Mun", "Mây", "Luna", "Bơ Bơ", "Simba", "Nhím", "Cam" };

            string phone;
            do phone = "09" + _r.Next(10_000_000, 99_999_999); while (!_phones.Add(phone));
            var o = new Owner
            {
                Code = $"KH{++_ownerSeq:00000}", FullName = $"{Pick(last)} {Pick(mid)} {Pick(first)}", Phone = phone,
                Address = $"{_r.Next(1, 600)} {Pick(streets)}, Quận 7, TP. HCM", CreatedAt = d.AddHours(_r.Next(8, 12)),
                Email = Chance(0.4) ? $"kh{_ownerSeq}@gmail.com" : null
            };
            _db.Owners.Add(o);
            var isCat = _catSpecies.HasValue && Chance(0.4);
            var speciesId = isCat ? _catSpecies : _dogSpecies;
            var young = Chance(0.35);
            var birth = young ? d.AddDays(-_r.Next(55, 110)) : d.AddMonths(-_r.Next(6, 120));
            var weight = isCat ? Math.Round((decimal)(young ? 0.8 + _r.NextDouble() : 2.5 + _r.NextDouble() * 3), 1)
                               : Math.Round((decimal)(young ? 1.5 + _r.NextDouble() * 3 : 3 + _r.NextDouble() * 25), 1);
            var pet = new Pet
            {
                Code = $"TC{++_petSeq:00000}", Name = isCat ? Pick(catNames) : Pick(dogNames), Owner = o, SpeciesId = speciesId,
                BreedId = speciesId.HasValue && _breeds.TryGetValue(speciesId.Value, out var b) && b.Count > 0 ? Pick(b) : null,
                Gender = Chance(0.5) ? PetGender.Male : PetGender.Female, BirthDate = birth, WeightKg = weight,
                Color = Pick(new[] { "Vàng", "Đen", "Trắng", "Nâu", "Tam thể", "Xám", "Vàng trắng", "Mướp" }), CreatedAt = o.CreatedAt
            };
            if (Chance(0.06)) pet.Allergies = "Dị ứng kháng sinh nhóm Penicillin";
            _db.Pets.Add(pet);
            await _db.SaveChangesAsync();
            _newOwners++; _newPets++;
            var info = new PetInfo
            {
                Id = pet.Id, OwnerId = o.Id, OwnerName = o.FullName, Name = pet.Name, SpeciesId = speciesId ?? 0, IsDog = !isCat && speciesId != null, IsCat = isCat,
                Gender = pet.Gender, Weight = weight, BirthDate = birth
            };
            _pets.Add(info);
            return info;
        }

        // ---------------------------------------------------------------------------------------------
        /// <summary>Một lượt khách: lịch hẹn (nếu có) → phiếu khám → hóa đơn → thu tiền</summary>
        private async Task EncounterAsync(DateTime d, DateTime at, Plan p, int vetId, bool booked, bool isToday)
        {
            var reason = p.Reason;
            Scenario? sc = null;
            if (p.Kind == PlanKind.Exam)
            {
                var pool = Scenarios.Where(s => s.Species == 0 || (s.Species == 1 && p.Pet.IsDog) || (s.Species == 2 && p.Pet.IsCat)).ToList();
                sc = Pick(pool);
                if (sc.Severe && !Chance(0.5)) sc = pool.First(s => !s.Severe);
                reason = sc.Reason;
            }

            Appointment? appt = null;
            if (booked)
            {
                var svc = p.Kind switch
                {
                    PlanKind.Vaccine => _svc.GetValueOrDefault("TP01"), PlanKind.Recheck => _svc.GetValueOrDefault("KB02"),
                    PlanKind.Grooming => _svc.GetValueOrDefault(p.Pet.IsCat ? "SP04" : "SP01"), PlanKind.Surgery => SurgeryService(p.Pet), _ => _exam
                } ?? _exam;
                appt = new Appointment
                {
                    Code = await CodeAsync("LH", at), OwnerId = p.Pet.OwnerId, PetId = p.Pet.Id, VetId = vetId, ServiceItemId = svc.Id, StartAt = at,
                    DurationMinutes = Math.Max(15, svc.DurationMinutes), Reason = reason, Status = AppointmentStatus.Confirmed,
                    CreatedById = _reception, CreatedAt = at.Date.AddDays(-_r.Next(1, 7)).AddHours(_r.Next(8, 18))
                };
                _db.Appointments.Add(appt);
                _appts++;
                if (isToday && at > _now) appt.Status = Chance(0.5) ? AppointmentStatus.Confirmed : AppointmentStatus.Booked;
                await _db.SaveChangesAsync();
                if (isToday && at > _now) return;
                // Khách không đến / hủy lịch: tái khám, tiêm nhắc dời sang vài ngày sau
                if (Chance(p.Kind == PlanKind.Surgery ? 0.03 : 0.08))
                {
                    var cancel = Chance(0.35);
                    appt.Status = cancel ? AppointmentStatus.Cancelled : AppointmentStatus.NoShow;
                    if (cancel) appt.Note = "Khách gọi báo bận, hẹn lại sau";
                    _noShows++;
                    await _db.SaveChangesAsync();
                    if (p.Kind is PlanKind.Vaccine or PlanKind.Recheck) AddPlan(d.AddDays(_r.Next(2, 6)), p);
                    return;
                }
            }
            else if (isToday && at > _now) return;

            // Hôm nay: lượt mới đến còn trong hàng chờ / đang khám
            var status = VisitStatus.Completed;
            if (isToday && at > _now.AddMinutes(-40)) status = at > _now.AddMinutes(-15) ? VisitStatus.Waiting : VisitStatus.InProgress;
            if (appt != null) appt.Status = status == VisitStatus.Completed ? AppointmentStatus.Completed : AppointmentStatus.CheckedIn;

            if (p.Kind == PlanKind.Grooming)
            {
                // Spa không lập phiếu khám: hóa đơn dịch vụ trực tiếp
                await _db.SaveChangesAsync();
                if (status != VisitStatus.Completed) return;
                var lines = new List<InvoiceLine>();
                var big = p.Pet.Weight >= 10;
                if (p.Pet.IsCat) AddSvc(lines, "SP04", null); else AddSvc(lines, big ? "SP02" : "SP01", null);
                if (p.Pet.IsDog && Chance(0.4)) AddSvc(lines, "SP03", null);
                AddSvc(lines, "SP05", null);
                if (Chance(0.3)) AddPrd(lines, p.Pet.IsCat ? "PK02" : "PK03", 1, null);
                if (Chance(0.3)) AddPrd(lines, p.Pet.IsCat ? "TA02" : "TA01", 1, null);
                if (lines.Count == 0) return;
                await BillAsync(Cap(at.AddMinutes(75)), p.Pet, null, null, lines, isToday, "Dịch vụ spa");
                return;
            }

            var visit = new Visit
            {
                Code = await CodeAsync("PK", at), PetId = p.Pet.Id, OwnerId = p.Pet.OwnerId, VetId = vetId, VisitAt = at, Status = status,
                Reason = reason, AppointmentId = appt?.Id, CreatedById = _reception, CreatedAt = at,
                WeightKg = DriftWeight(p.Pet), Temperature = 38.0m + _r.Next(0, 16) / 10m,
                HeartRate = p.Pet.IsCat ? _r.Next(140, 200) : _r.Next(70, 140), RespiratoryRate = _r.Next(16, 36)
            };
            _db.Visits.Add(visit);
            await _db.SaveChangesAsync();
            if (appt != null) appt.VisitId = visit.Id;
            _visits++;
            if (status == VisitStatus.Waiting) { await _db.SaveChangesAsync(); return; }
            visit.StartedAt = at.AddMinutes(_r.Next(3, 15));

            var bill = new List<InvoiceLine>();
            switch (p.Kind)
            {
                case PlanKind.Vaccine: await VaccinateAsync(visit, p, vetId, at, bill); break;
                case PlanKind.Surgery: await SurgeryAsync(visit, p, vetId, at, bill); break;
                case PlanKind.Recheck:
                    visit.Symptoms = "Đã hết triệu chứng chính sau đợt điều trị";
                    visit.Examination = "Thể trạng ổn định, ăn uống bình thường";
                    visit.Diagnosis = p.Diagnosis != null ? $"{p.Diagnosis} – đã ổn định" : "Tái khám – ổn định";
                    visit.Treatment = "Ngưng thuốc, theo dõi tại nhà";
                    visit.Advice = "Tái khám ngay nếu triệu chứng quay lại";
                    AddVisitSvc(visit, bill, "KB02", vetId);
                    break;
                default: Examine(visit, sc!, vetId, at, bill); break;
            }

            if (status == VisitStatus.InProgress) { await _db.SaveChangesAsync(); return; }
            visit.CompletedAt = Cap(at.AddMinutes(_r.Next(20, 50)));
            await _db.SaveChangesAsync();

            // Ca nặng: chuyển nằm viện, hóa đơn khám lập ngay, tiền nội trú tính khi xuất viện
            if (sc is { Severe: true }) await HospitalizeAsync(visit, p.Pet, vetId, sc, visit.CompletedAt.Value);
            if (sc is { RecheckDays: > 0 } && !sc.Severe)
            {
                visit.FollowUpDate = d.AddDays(sc.RecheckDays);
                AddPlan(visit.FollowUpDate.Value, new Plan(PlanKind.Recheck, p.Pet, vetId, $"Tái khám {sc.Diagnosis.ToLower()}", Diagnosis: sc.Diagnosis));
            }
            var inv = await BillAsync(Cap(visit.CompletedAt.Value.AddMinutes(5)), p.Pet, visit.Id, null, bill, isToday, $"Phiếu khám {visit.Code}");
            if (inv != null) visit.InvoiceId = inv.Id;
            await _db.SaveChangesAsync();
        }

        private decimal DriftWeight(PetInfo pet)
        {
            var growth = pet.BirthDate.HasValue && pet.BirthDate > _today.AddMonths(-12) ? (decimal)(_r.NextDouble() * 0.06) : (decimal)((_r.NextDouble() - 0.5) * 0.04);
            pet.Weight = Math.Max(0.3m, Math.Round(pet.Weight * (1 + growth), 1));
            return pet.Weight;
        }

        private void AddVisitSvc(Visit v, List<InvoiceLine> bill, string code, int vetId)
        {
            if (!_svc.TryGetValue(code, out var s)) return;
            v.Services.Add(new VisitService { ServiceItemId = s.Id, Quantity = 1, UnitPrice = s.Price });
            AddSvc(bill, code, vetId);
        }

        private void AddVisitMed(Visit v, List<InvoiceLine> bill, string code, decimal qty, string? dose, int? days, int vetId)
        {
            if (!_prd.TryGetValue(code, out var m)) return;
            v.Medicines.Add(new VisitMedicine { ProductId = m.Id, Quantity = qty, UnitPrice = m.Price, Dosage = dose, Days = days });
            AddPrd(bill, code, qty, vetId);
        }

        private void AddSvc(List<InvoiceLine> lines, string code, int? vetId)
        {
            if (!_svc.TryGetValue(code, out var s)) return;
            lines.Add(new InvoiceLine { Kind = InvoiceLineKind.Service, ServiceItemId = s.Id, Description = s.Name, Unit = s.Unit, Quantity = 1, UnitPrice = s.Price, VetId = vetId });
        }

        private void AddPrd(List<InvoiceLine> lines, string code, decimal qty, int? vetId, string? desc = null)
        {
            if (!_prd.TryGetValue(code, out var p)) return;
            lines.Add(new InvoiceLine { Kind = InvoiceLineKind.Product, ProductId = p.Id, Description = desc ?? p.Name, Unit = p.Unit, Quantity = qty, UnitPrice = p.Price, VetId = vetId });
        }

        private void Examine(Visit v, Scenario sc, int vetId, DateTime at, List<InvoiceLine> bill)
        {
            v.Symptoms = sc.Symptoms; v.Examination = sc.Exam; v.Diagnosis = sc.Diagnosis; v.Treatment = sc.Treatment; v.Advice = sc.Advice;
            if (sc.Severe) v.Temperature = 39.6m + _r.Next(0, 6) / 10m;
            foreach (var code in sc.Services) AddVisitSvc(v, bill, code, vetId);
            foreach (var m in sc.Meds) AddVisitMed(v, bill, m.Code, m.Qty, m.Dose, m.Days, vetId);
        }

        private ServiceItem? SurgeryService(PetInfo pet) =>
            _svc.GetValueOrDefault(pet.IsCat ? (pet.Gender == PetGender.Female ? "PT04" : "PT03") : (pet.Gender == PetGender.Female ? "PT02" : "PT01"));

        private async Task SurgeryAsync(Visit v, Plan p, int vetId, DateTime at, List<InvoiceLine> bill)
        {
            var s = SurgeryService(p.Pet);
            v.Examination = "Xét nghiệm máu trước mổ trong giới hạn bình thường"; v.Diagnosis = "Đủ điều kiện phẫu thuật";
            v.Treatment = (s?.Name ?? "Triệt sản") + ", gây mê hô hấp"; v.Advice = "Đeo loa chống liếm 7 ngày, cắt chỉ sau 10 ngày";
            AddVisitSvc(v, bill, "XN01", vetId);
            if (s != null) { v.Services.Add(new VisitService { ServiceItemId = s.Id, Quantity = 1, UnitPrice = s.Price }); AddSvc(bill, s.Code, vetId); }
            AddVisitMed(v, bill, "TH01", 10, "Uống 1 viên x 2 lần/ngày", 5, vetId);
            AddVisitMed(v, bill, "TH04", 1, "Uống 0.1ml/kg/ngày", 3, vetId);
            AddVisitMed(v, bill, "VT04", 1, "Đeo 7–10 ngày", null, vetId);
            v.FollowUpDate = at.Date.AddDays(10);
            AddPlan(v.FollowUpDate.Value, new Plan(PlanKind.Recheck, p.Pet, vetId, "Cắt chỉ sau triệt sản", Diagnosis: "Hậu phẫu triệt sản"));
            p.Pet.Neutered = true;
            await _db.Pets.Where(x => x.Id == p.Pet.Id).ExecuteUpdateAsync(x => x.SetProperty(y => y.Neutered, true));
        }

        private async Task VaccinateAsync(Visit v, Plan p, int vetId, DateTime at, List<InvoiceLine> bill)
        {
            (int Id, string Code)? vx = p.VaccineId is { } vid && _codeById.TryGetValue(vid, out var vcode) ? (vid, vcode) : null;
            if (vx == null)
            {
                var useRabies = p.Pet.IsDog && !p.Pet.Young(at) && Chance(0.5);
                vx = p.Pet.IsCat ? _catCore : useRabies ? _rabies : _dogCore;
                vx ??= _dogCore ?? _catCore ?? _rabies;
            }
            v.Examination = "Thể trạng tốt, đủ điều kiện tiêm"; v.Diagnosis = "Khỏe mạnh – tiêm phòng";
            v.Temperature = 38.3m + _r.Next(0, 5) / 10m;
            AddVisitSvc(v, bill, "TP01", vetId);
            if (vx == null) return;
            var (id, code) = vx.Value;
            var prod = _prd[code];
            var dose = _doses.GetValueOrDefault((p.Pet.Id, id)) + 1;
            _doses[(p.Pet.Id, id)] = dose;
            v.Reason = $"Tiêm phòng mũi {dose}";
            // Lịch mũi: mũi cơ bản cách 21 ngày (tối đa 3 mũi), sau đó nhắc lại hằng năm
            var next = dose < 3 && (prod.Booster ?? 365) < 60 ? at.Date.AddDays(prod.Booster ?? 21) : at.Date.AddDays(365);
            await _db.Vaccinations.Where(x => x.PetId == p.Pet.Id && x.ProductId == id && !x.ReminderDone)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.ReminderDone, true));
            _db.Vaccinations.Add(new Vaccination
            {
                PetId = p.Pet.Id, ProductId = id, VaccineName = prod.Name, DoseNumber = dose, Date = at.Date, VetId = vetId, Visit = v,
                NextDueDate = next, LotNo = $"L{at:yyMM}{_r.Next(100, 999)}", CreatedAt = at
            });
            _vaccinations++;
            AddPrd(bill, code, 1, vetId, $"{prod.Name} (mũi {dose})");
            // Chủ nuôi thường tiêm mũi kế tiếp gần đúng hẹn
            if (next < _today.AddDays(8) && Chance(0.8))
                AddPlan(next.AddDays(_r.Next(0, 3)), new Plan(PlanKind.Vaccine, p.Pet, vetId, $"Tiêm nhắc lại mũi {dose + 1}", id, dose + 1));
        }

        // ---------------------------------------------------------------------------------------------
        private (int Id, string Code, decimal Rate)? FreeCage(DateTime from, bool isolation, decimal weight)
        {
            var size = weight < 8 ? "Nhỏ" : weight < 20 ? "Vừa" : "Lớn";
            var free = _cages.Where(c => c.Iso == isolation && (!_cageBusyUntil.TryGetValue(c.Id, out var until) || until <= from)).ToList();
            if (free.Count == 0) free = _cages.Where(c => !_cageBusyUntil.TryGetValue(c.Id, out var until) || until <= from).ToList();
            if (free.Count == 0) return null;
            var c = free.FirstOrDefault(c => c.Size == size);
            if (c.Id == 0) c = free[0];
            return (c.Id, c.Code, c.Rate);
        }

        private async Task HospitalizeAsync(Visit v, PetInfo pet, int vetId, Scenario sc, DateTime at)
        {
            var cage = FreeCage(at, sc.Species == 1, pet.Weight);
            if (cage == null) return;
            var stay = _r.Next(2, 6);
            var discharge = at.Date.AddDays(stay).AddHours(_r.Next(9, 17));
            var a = new Admission
            {
                Code = await CodeAsync("NT", at), Kind = AdmissionKind.Hospitalization, PetId = pet.Id, OwnerId = pet.OwnerId, CageId = cage.Value.Id, VetId = vetId,
                VisitId = v.Id, AdmitAt = at.AddMinutes(10), ExpectedDischarge = discharge.Date, Reason = sc.Diagnosis,
                CareNote = sc.Species == 1 ? "Cách ly, truyền dịch 2 lần/ngày, khử trùng chuồng" : "Truyền dịch, theo dõi lượng nước tiểu",
                DailyRate = cage.Value.Rate, CreatedById = vetId, CreatedAt = at.AddMinutes(10)
            };
            v.Advice = "Nằm viện điều trị, theo dõi hằng ngày";
            // Theo dõi sáng – chiều, nhiệt độ giảm dần khi đáp ứng điều trị
            var temp = v.Temperature ?? 39.8m;
            string[] appetite = { "Bỏ ăn", "Ăn ít", "Ăn được 1/2 khẩu phần", "Ăn tốt" };
            var k = 0;
            for (var t = at.Date.AddHours(18); t < discharge && t <= _now; t = t.AddHours(t.Hour == 8 ? 10 : 14), k++)
            {
                temp = Math.Max(38.4m, temp - 0.2m - _r.Next(0, 3) / 10m);
                a.Notes.Add(new AdmissionNote
                {
                    At = t, Temperature = temp, WeightKg = k % 2 == 0 ? pet.Weight : null, Appetite = appetite[Math.Min(k / 2, 3)],
                    Note = k < 2 ? "Còn mệt, theo dõi sát" : "Tỉnh táo, đáp ứng điều trị tốt", Treatment = k < 3 ? "Truyền dịch, kháng sinh tiêm" : "Kháng sinh đường uống",
                    UserId = Chance(0.7) ? vetId : Pick(_vets)
                });
            }
            _db.Admissions.Add(a);
            _admissions++;
            _cageBusyUntil[cage.Value.Id] = discharge > _now ? DateTime.MaxValue : discharge;
            await _db.SaveChangesAsync();
            if (discharge > _now) return; // Còn nằm viện tới hôm nay

            a.Status = AdmissionStatus.Discharged; a.DischargeAt = discharge; a.DischargeNote = "Ổn định, ăn uống tốt, xuất viện tiếp tục điều trị tại nhà";
            var days = a.BillableDays();
            var lines = new List<InvoiceLine>
            {
                new() { Kind = InvoiceLineKind.Other, Description = $"{a.Kind.GetDisplayName()} – chuồng {cage.Value.Code} ({a.AdmitAt:dd/MM HH:mm} → {discharge:dd/MM HH:mm})",
                    Unit = "Ngày", Quantity = days, UnitPrice = a.DailyRate, VetId = vetId }
            };
            if (_svc.TryGetValue("DT01", out var fluid)) lines.Add(new InvoiceLine { Kind = InvoiceLineKind.Service, ServiceItemId = fluid.Id, Description = fluid.Name, Unit = fluid.Unit, Quantity = days, UnitPrice = fluid.Price, VetId = vetId });
            AddPrd(lines, "TH13", days * 2, vetId);
            AddPrd(lines, "TH14", days * 2, vetId);
            AddPrd(lines, "VT02", 1, vetId);
            var inv = await BillAsync(discharge, pet, null, a.Id, lines, false, $"Phiếu nằm viện điều trị {a.Code}");
            if (inv != null) a.InvoiceId = inv.Id;
            await _db.SaveChangesAsync();
        }

        private async Task BoardingAsync(DateTime at, PetInfo pet)
        {
            if (at > _now) return;
            var cage = FreeCage(at, false, pet.Weight);
            if (cage == null) return;
            var discharge = at.Date.AddDays(_r.Next(1, 7)).AddHours(_r.Next(9, 19));
            var a = new Admission
            {
                Code = await CodeAsync("NT", at), Kind = AdmissionKind.Boarding, PetId = pet.Id, OwnerId = pet.OwnerId, CageId = cage.Value.Id,
                AdmitAt = at, ExpectedDischarge = discharge.Date, Reason = Pick(new[] { "Chủ đi công tác", "Gia đình đi du lịch", "Sửa nhà", "Chủ về quê" }),
                CareNote = Pick(new[] { "Gửi kèm thức ăn hạt, cho ăn 2 bữa/ngày", "Dắt đi dạo 2 lần/ngày", "Gửi kèm đồ chơi, ổ nằm" }),
                DailyRate = cage.Value.Rate, CreatedById = _reception, CreatedAt = at
            };
            for (var t = at.Date.AddDays(1).AddHours(9); t < discharge && t <= _now; t = t.AddDays(1))
                a.Notes.Add(new AdmissionNote { At = t, Appetite = Chance(0.85) ? "Ăn tốt" : "Ăn ít", Note = "Vui vẻ, đi vệ sinh bình thường", UserId = _reception });
            _db.Admissions.Add(a);
            _admissions++;
            _cageBusyUntil[cage.Value.Id] = discharge > _now ? DateTime.MaxValue : discharge;
            await _db.SaveChangesAsync();
            if (discharge > _now) return;

            a.Status = AdmissionStatus.Discharged; a.DischargeAt = discharge; a.DischargeNote = "Khỏe mạnh, ăn uống tốt";
            var lines = new List<InvoiceLine>
            {
                new() { Kind = InvoiceLineKind.Other, Description = $"{a.Kind.GetDisplayName()} – chuồng {cage.Value.Code} ({at:dd/MM HH:mm} → {discharge:dd/MM HH:mm})",
                    Unit = "Ngày", Quantity = a.BillableDays(), UnitPrice = a.DailyRate }
            };
            if (Chance(0.4)) AddSvc(lines, pet.IsCat ? "SP04" : pet.Weight >= 10 ? "SP02" : "SP01", null);
            var inv = await BillAsync(discharge, pet, null, a.Id, lines, false, $"Phiếu lưu chuồng {a.Code}");
            if (inv != null) a.InvoiceId = inv.Id;
            await _db.SaveChangesAsync();
        }

        private async Task RetailAsync(DateTime at)
        {
            var lines = new List<InvoiceLine>();
            var codes = new[] { "TA01", "TA02", "TA03", "TA04", "PK01", "PK02", "PK03", "TH10", "TH11", "TH08", "TH09" }.Where(_prd.ContainsKey).ToList();
            if (codes.Count == 0) return;
            foreach (var c in codes.OrderBy(_ => _r.Next()).Take(_r.Next(1, 4)))
                AddPrd(lines, c, _prd[c].Unit is "Gói" or "Lon" or "Viên" ? _r.Next(2, 10) : 1, null);
            var pet = Chance(0.5) && _pets.Count > 0 ? Pick(_pets) : null;
            var inv = await BillAsync(at, pet, null, null, lines, false, "Bán lẻ", pet == null ? "Khách lẻ" : null, allowDebt: false);
            // Thỉnh thoảng hủy hóa đơn bán lẻ (khách đổi ý) – hoàn nhập kho, ghi hoàn tiền
            if (inv != null && inv.Status != InvoiceStatus.Draft && Chance(0.04)) await CancelAsync(inv, at.AddMinutes(20), "Khách đổi ý, trả lại hàng");
        }

        // ---------------------------------------------------------------------------------------------
        /// <summary>Lập hóa đơn → phát hành (xuất kho FEFO, ghi giá vốn) → thu tiền ngay / trả sau / còn nợ</summary>
        private async Task<Invoice?> BillAsync(DateTime at, PetInfo? pet, int? visitId, int? admissionId, List<InvoiceLine> lines, bool isToday, string note,
            string? customerName = null, bool allowDebt = true)
        {
            if (lines.Count == 0) return null;
            var inv = new Invoice
            {
                Code = await CodeAsync("HD", at), Date = at, OwnerId = pet?.OwnerId, PetId = pet?.Id, VisitId = visitId, AdmissionId = admissionId,
                CustomerName = customerName, CreatedById = _cashier, CreatedAt = at, Note = note
            };
            foreach (var l in lines) inv.Lines.Add(l);
            // Khách quen được giảm 5% tiền dịch vụ
            if (pet != null && Chance(0.06))
                inv.Discount = Math.Round(lines.Where(l => l.Kind == InvoiceLineKind.Service).Sum(l => l.Quantity * l.UnitPrice) * 0.05m / 1000m, 0) * 1000m;
            inv.Recalc();
            // Hôm nay: một phần hóa đơn còn nháp chờ thu ngân
            if (isToday && at > _now.AddHours(-2) && Chance(0.5))
            {
                _db.Invoices.Add(inv);
                await _db.SaveChangesAsync();
                _invoices++;
                return inv;
            }

            await EnsureStockAsync(at, lines);
            _db.Invoices.Add(inv);
            await _db.SaveChangesAsync();
            var ids = inv.Lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
            var products = await _db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            foreach (var l in inv.Lines.Where(l => l.ProductId.HasValue))
                l.CostAmount = await _stock.IssueAsync(products[l.ProductId!.Value], l.Quantity, StockMoveType.Sale, nameof(Invoice), inv.Id, inv.Code, customerName ?? pet?.OwnerName);
            StampMoves(at);
            inv.IssuedAt = at;
            inv.Status = InvoiceStatus.Unpaid;
            _invoices++;
            _revenue += inv.Total;

            // 85% trả đủ ngay, 8% trả trước một phần, 7% ghi nợ – phần còn lại thu trong 1–10 ngày
            var x = _r.Next(100);
            var method = RandomMethod();
            decimal pay = !allowDebt || x < 85 ? inv.Total : x < 93 ? Math.Round(inv.Total * 0.5m / 10000m, 0) * 10000m : 0;
            if (pay > 0) AddPayment(inv, pay, method, Cap(at.AddMinutes(5)), null);
            InvoiceService.UpdateStatus(inv);
            if (inv.Balance > 0)
            {
                var when = at.Date.AddDays(_r.Next(1, 11));
                if (!_laterPays.TryGetValue(when, out var l)) _laterPays[when] = l = new();
                l.Add((inv.Id, RandomMethod()));
            }
            await _db.SaveChangesAsync();
            return inv;
        }

        private void AddPayment(Invoice inv, decimal amount, PaymentMethod method, DateTime at, string? note)
        {
            _db.Payments.Add(new Payment { InvoiceId = inv.Id, Amount = amount, Method = method, At = at, UserId = _cashier, Note = note });
            inv.Paid += amount;
            _payments++;
        }

        private async Task CollectLaterPaymentsAsync(DateTime d)
        {
            if (!_laterPays.Remove(d, out var list)) return;
            foreach (var (id, method) in list)
            {
                var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id);
                if (inv == null || inv.Status is InvoiceStatus.Cancelled or InvoiceStatus.Paid or InvoiceStatus.Draft || inv.Balance <= 0) continue;
                var at = d.AddHours(_r.Next(9, 19)).AddMinutes(_r.Next(0, 60));
                if (at > _now) continue; // Hẹn trả sau giờ hiện tại → vẫn còn trong công nợ
                AddPayment(inv, inv.Balance, method, at, "Khách thanh toán phần còn lại");
                InvoiceService.UpdateStatus(inv);
            }
            await _db.SaveChangesAsync();
        }

        private async Task CancelAsync(Invoice inv, DateTime at, string reason)
        {
            await _stock.ReturnAsync(nameof(Invoice), inv.Id, inv.Code, "Hủy hóa đơn: " + reason);
            StampMoves(at);
            if (inv.Paid > 0)
            {
                _db.Payments.Add(new Payment { InvoiceId = inv.Id, Amount = -inv.Paid, Method = PaymentMethod.Cash, Note = "Hoàn tiền do hủy hóa đơn", UserId = _cashier, At = at });
                inv.Paid = 0;
            }
            inv.Status = InvoiceStatus.Cancelled; inv.CancelledAt = at; inv.CancelReason = reason;
            _revenue -= inv.Total;
            _cancelled++;
            await _db.SaveChangesAsync();
        }

        // ---------------------------------------------------------------------------------------------
        /// <summary>Lịch hẹn 7 ngày tới: tái khám, mũi nhắc lại đã hẹn + khách đặt mới</summary>
        private async Task BookUpcomingAsync(DateTime end)
        {
            var created = _now;
            foreach (var (date, plans) in _plans.Where(x => x.Key > end).OrderBy(x => x.Key).ToList())
                foreach (var p in plans)
                    await BookAsync(date.AddHours(_r.Next(8, 18)).AddMinutes(_r.Next(0, 4) * 15), p, created);
            for (var i = 1; i <= 7; i++)
            {
                var d = _today.AddDays(i);
                for (var k = _r.Next(1, 4); k > 0 && _pets.Count > 0; k--)
                {
                    var pet = Pick(_pets);
                    var kind = Chance(0.4) ? PlanKind.Grooming : Chance(0.5) ? PlanKind.Vaccine : PlanKind.Exam;
                    await BookAsync(d.AddHours(_r.Next(8, 18)).AddMinutes(_r.Next(0, 4) * 15),
                        new Plan(kind, pet, null, kind == PlanKind.Grooming ? "Tắm, cắt tỉa lông" : kind == PlanKind.Vaccine ? "Tiêm phòng nhắc lại" : "Khám định kỳ"), created);
                }
            }
            await _db.SaveChangesAsync();
        }

        private async Task BookAsync(DateTime at, Plan p, DateTime created)
        {
            var svc = p.Kind switch
            {
                PlanKind.Vaccine => _svc.GetValueOrDefault("TP01"), PlanKind.Recheck => _svc.GetValueOrDefault("KB02"),
                PlanKind.Grooming => _svc.GetValueOrDefault(p.Pet.IsCat ? "SP04" : "SP01"), PlanKind.Surgery => SurgeryService(p.Pet), _ => _exam
            } ?? _exam;
            _db.Appointments.Add(new Appointment
            {
                Code = await CodeAsync("LH", at), OwnerId = p.Pet.OwnerId, PetId = p.Pet.Id, VetId = p.VetId ?? Pick(_vets), ServiceItemId = svc.Id, StartAt = at,
                DurationMinutes = Math.Max(15, svc.DurationMinutes), Reason = p.Reason, Status = Chance(0.5) ? AppointmentStatus.Confirmed : AppointmentStatus.Booked,
                CreatedById = _reception, CreatedAt = created
            });
            _appts++;
        }

        /// <summary>Dữ liệu chèn lùi ngày → tính lại cột "tồn sau" trên thẻ kho theo thứ tự thời gian</summary>
        private async Task RebuildStockBalancesAsync()
        {
            var moves = await _db.StockMoves.OrderBy(m => m.ProductId).ThenBy(m => m.At).ThenBy(m => m.Id).ToListAsync();
            foreach (var g in moves.GroupBy(m => m.ProductId))
            {
                decimal bal = 0;
                foreach (var m in g) { bal += m.Quantity; m.Balance = bal; }
            }
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }
    }
}
