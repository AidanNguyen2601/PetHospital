using PetHospital.Web.Infrastructure;
using PetHospital.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PetHospital.Web.Data;

/// <summary>
/// Dữ liệu mẫu (cố định, lần nào cũng như nhau): nhân sự theo vai trò, dịch vụ – bảng giá, thuốc & vắc-xin có tồn kho theo lô,
/// ~30 khách hàng / ~50 thú cưng, 60 ngày lịch sử khám – hóa đơn, lịch hẹn hôm nay, nội trú, nhắc tiêm.
/// Mật khẩu: admin/Admin@123, các tài khoản khác Pet@123.
/// </summary>
public static class SampleData
{
    public static async Task SeedAsync(PetDbContext db, StockService stock, IPasswordHasher<AppUser> hasher)
    {
        var today = DateTime.Today;
        var rnd = new Random(2026);

        // ---------- Nhân sự ----------
        AppUser U(string username, string fullName, string roles, bool vet = false, string? title = null, string pwd = "Pet@123")
        {
            var u = new AppUser { Username = username, FullName = fullName, Roles = roles, IsVet = vet, Title = title, Email = $"{username.Replace(".", "")}@petcare.local",
                Phone = "09" + rnd.Next(10000000, 99999999) };
            u.PasswordHash = hasher.HashPassword(u, pwd);
            db.Users.Add(u);
            return u;
        }
        var uAdmin = U("admin", "Quản trị hệ thống", AppRoles.Admin, pwd: "Admin@123");
        var uManager = U("quanly", "Phạm Thu Hương", $"{AppRoles.Manager},{AppRoles.Vet}", true, "ThS. BS.");
        var vAn = U("bs.an", "Nguyễn Thanh An", AppRoles.Vet, true, "BS.");
        var vBinh = U("bs.binh", "Trần Bảo Bình", AppRoles.Vet, true, "BS.");
        var vChau = U("bs.chau", "Lê Minh Châu", AppRoles.Vet, true, "BS.");
        var uReception = U("letan", "Võ Ngọc Lan", AppRoles.Receptionist);
        var uCashier = U("thungan", "Đặng Thị Mai", AppRoles.Cashier);
        var uStock = U("kho", "Huỳnh Văn Phúc", AppRoles.Warehouse);
        await db.SaveChangesAsync();
        var vets = new[] { vAn, vBinh, vChau, uManager };
        // Chi nhánh: quản trị + quản lý làm toàn chuỗi, nhân viên thuộc chi nhánh mặc định
        var mainBranch = db.WriteBranchId;
        uAdmin.AllBranches = true; uManager.AllBranches = true;
        foreach (var u in new[] { vAn, vBinh, vChau, uReception, uCashier, uStock }) db.UserBranches.Add(new UserBranch { UserId = u.Id, BranchId = mainBranch });
        await db.SaveChangesAsync();
        var bc = await db.WriteBranchCodeAsync();

        // ---------- Dịch vụ ----------
        ServiceItem S(string code, string name, ServiceKind kind, decimal price, int minutes = 30, string unit = "Lần")
        {
            var s = new ServiceItem { Code = code, Name = name, Kind = kind, Price = price, DurationMinutes = minutes, Unit = unit };
            db.ServiceItems.Add(s);
            return s;
        }
        var sExam = S("KB01", "Khám tổng quát", ServiceKind.Exam, 150_000, 20);
        var sRecheck = S("KB02", "Tái khám", ServiceKind.Exam, 80_000, 15);
        S("KB03", "Khám ngoài giờ / cấp cứu", ServiceKind.Exam, 300_000, 30);
        var sVaccine = S("TP01", "Công tiêm phòng (khám trước tiêm)", ServiceKind.Vaccination, 50_000, 15);
        var sBlood = S("XN01", "Xét nghiệm công thức máu", ServiceKind.Lab, 350_000, 30);
        var sBio = S("XN02", "Xét nghiệm sinh hóa máu", ServiceKind.Lab, 450_000, 30);
        var sTest = S("XN03", "Test nhanh Care / Parvo", ServiceKind.Lab, 250_000, 15);
        var sSkin = S("XN04", "Soi da, soi ký sinh trùng", ServiceKind.Lab, 120_000, 15);
        S("XN05", "Xét nghiệm nước tiểu", ServiceKind.Lab, 200_000, 20);
        var sUltra = S("HA01", "Siêu âm ổ bụng", ServiceKind.Imaging, 300_000, 20);
        var sXray = S("HA02", "Chụp X-quang (1 phim)", ServiceKind.Imaging, 350_000, 20, "Phim");
        var sNeuterDogM = S("PT01", "Triệt sản chó đực", ServiceKind.Surgery, 1_200_000, 90);
        S("PT02", "Triệt sản chó cái", ServiceKind.Surgery, 1_800_000, 120);
        var sNeuterCatM = S("PT03", "Triệt sản mèo đực", ServiceKind.Surgery, 600_000, 45);
        var sNeuterCatF = S("PT04", "Triệt sản mèo cái", ServiceKind.Surgery, 900_000, 60);
        var sDental = S("PT05", "Cạo vôi răng", ServiceKind.Surgery, 600_000, 60);
        var sSuture = S("PT06", "Xử lý, khâu vết thương", ServiceKind.Surgery, 300_000, 30);
        var sFluid = S("DT01", "Truyền dịch", ServiceKind.Treatment, 200_000, 60);
        var sInject = S("DT02", "Tiêm thuốc điều trị", ServiceKind.Treatment, 60_000, 10, "Mũi");
        var sEar = S("DT03", "Vệ sinh tai", ServiceKind.Treatment, 80_000, 15);
        var sBathS = S("SP01", "Tắm vệ sinh chó dưới 10kg", ServiceKind.Grooming, 150_000, 60);
        var sBathL = S("SP02", "Tắm vệ sinh chó trên 10kg", ServiceKind.Grooming, 250_000, 90);
        var sCut = S("SP03", "Cắt tỉa lông tạo kiểu", ServiceKind.Grooming, 350_000, 90);
        var sBathCat = S("SP04", "Tắm mèo", ServiceKind.Grooming, 200_000, 60);
        var sNail = S("SP05", "Cắt móng, vệ sinh tai", ServiceKind.Grooming, 50_000, 15);
        await db.SaveChangesAsync();

        // ---------- Thuốc, vắc-xin, hàng hóa ----------
        Product P(string code, string name, ProductKind kind, string unit, decimal sale, decimal cost, decimal min, string? usage = null, int? booster = null, string? ingredient = null)
        {
            var p = new Product { Code = code, Name = name, Kind = kind, Unit = unit, SalePrice = sale, CostPrice = cost, MinStock = min, DefaultUsage = usage,
                BoosterDays = booster, Ingredient = ingredient, TrackExpiry = kind is ProductKind.Medicine or ProductKind.Vaccine or ProductKind.Food };
            db.Products.Add(p);
            return p;
        }
        var vx7 = P("VX01", "Vắc-xin 7 bệnh chó (Recombitek C6/CV)", ProductKind.Vaccine, "Liều", 250_000, 140_000, 20, "Tiêm dưới da 1ml", 21, "Care, Parvo, Viêm gan, Ho cũi, Phó cúm, Lepto, Corona");
        var vxRabies = P("VX02", "Vắc-xin Dại (Rabisin)", ProductKind.Vaccine, "Liều", 120_000, 55_000, 20, "Tiêm dưới da 1ml", 365, "Virus dại bất hoạt");
        var vxCat = P("VX03", "Vắc-xin 4 bệnh mèo (Felocell 4)", ProductKind.Vaccine, "Liều", 280_000, 160_000, 15, "Tiêm dưới da 1ml", 21, "Giảm bạch cầu, Calici, Herpes, Chlamydia");
        var vx5 = P("VX04", "Vắc-xin 5 bệnh chó (Vanguard Plus 5)", ProductKind.Vaccine, "Liều", 220_000, 120_000, 15, "Tiêm dưới da 1ml", 21);
        var vxKennel = P("VX05", "Vắc-xin Ho cũi chó (Bronchi-Shield)", ProductKind.Vaccine, "Liều", 200_000, 110_000, 5, "Nhỏ mũi", 365);
        var amox = P("TH01", "Amoxicillin 250mg", ProductKind.Medicine, "Viên", 5_000, 1_800, 200, "Uống 2 lần/ngày sau ăn", ingredient: "Amoxicillin");
        var ceph = P("TH02", "Cephalexin 500mg", ProductKind.Medicine, "Viên", 6_000, 2_200, 200, "Uống 2 lần/ngày", ingredient: "Cephalexin");
        var metro = P("TH03", "Metronidazole 250mg", ProductKind.Medicine, "Viên", 3_000, 900, 150, "Uống 2 lần/ngày", ingredient: "Metronidazole");
        var melox = P("TH04", "Meloxicam 1.5mg/ml (chai 10ml)", ProductKind.Medicine, "Chai", 120_000, 65_000, 10, "Uống 0.1ml/kg/ngày", ingredient: "Meloxicam");
        var pred = P("TH05", "Prednisolone 5mg", ProductKind.Medicine, "Viên", 2_000, 600, 200, "Uống theo chỉ định", ingredient: "Prednisolone");
        var otomax = P("TH06", "Thuốc nhỏ tai Otomax 15g", ProductKind.Medicine, "Tuýp", 180_000, 105_000, 8, "Nhỏ tai 2 lần/ngày");
        var bravecto = P("TH07", "Bravecto chó 10–20kg", ProductKind.Medicine, "Viên", 650_000, 450_000, 6, "Uống 1 viên / 12 tuần", ingredient: "Fluralaner");
        var nexgard = P("TH08", "NexGard chó 4–10kg", ProductKind.Medicine, "Viên", 180_000, 115_000, 10, "Uống 1 viên / tháng", ingredient: "Afoxolaner");
        var revolution = P("TH09", "Revolution mèo 2.6–7.5kg", ProductKind.Medicine, "Tuýp", 220_000, 140_000, 10, "Nhỏ gáy 1 tuýp / tháng", ingredient: "Selamectin");
        var drontal = P("TH10", "Drontal Plus tẩy giun chó", ProductKind.Medicine, "Viên", 35_000, 18_000, 50, "1 viên / 10kg");
        var drontalCat = P("TH11", "Drontal tẩy giun mèo", ProductKind.Medicine, "Viên", 30_000, 15_000, 50, "1 viên / 4kg");
        var biolac = P("TH12", "Men tiêu hóa Biolac", ProductKind.Medicine, "Gói", 15_000, 6_000, 60, "1 gói / lần, 2 lần/ngày");
        var ringer = P("TH13", "Dịch truyền Lactate Ringer 500ml", ProductKind.Medicine, "Chai", 35_000, 14_000, 30);
        var enro = P("TH14", "Enrofloxacin 5% tiêm (lọ 50ml)", ProductKind.Medicine, "ml", 3_000, 1_000, 100, "Tiêm 1ml/10kg", ingredient: "Enrofloxacin");
        var nutri = P("TH15", "Gel dinh dưỡng Nutri-Plus 120g", ProductKind.Medicine, "Tuýp", 250_000, 160_000, 6);
        var syringe = P("VT01", "Bơm tiêm 3ml", ProductKind.Supply, "Cái", 3_000, 800, 200);
        var catheter = P("VT02", "Kim luồn 22G", ProductKind.Supply, "Cái", 15_000, 6_000, 50);
        var gauze = P("VT03", "Băng gạc y tế", ProductKind.Supply, "Cuộn", 12_000, 5_000, 40);
        var cone = P("VT04", "Loa chống liếm size M", ProductKind.Supply, "Cái", 60_000, 28_000, 10);
        var rcMini = P("TA01", "Royal Canin Mini Adult 2kg", ProductKind.Food, "Túi", 420_000, 330_000, 6);
        var rcKitten = P("TA02", "Royal Canin Kitten 2kg", ProductKind.Food, "Túi", 450_000, 350_000, 6);
        var pate = P("TA03", "Pate Whiskas 85g", ProductKind.Food, "Gói", 15_000, 10_000, 48);
        var hills = P("TA04", "Hill's Prescription Diet i/d (lon 156g)", ProductKind.Food, "Lon", 75_000, 52_000, 12);
        var seresto = P("PK01", "Vòng cổ trị ve rận Seresto", ProductKind.Accessory, "Cái", 650_000, 470_000, 3);
        var litter = P("PK02", "Cát vệ sinh mèo 10L", ProductKind.Accessory, "Bao", 150_000, 95_000, 10);
        var shampoo = P("PK03", "Sữa tắm SOS 530ml", ProductKind.Accessory, "Chai", 140_000, 85_000, 8);
        await db.SaveChangesAsync();
        var allProducts = await db.Products.ToListAsync();

        // ---------- Nhập kho (2 phiếu) ----------
        async Task Receive(string code, DateTime date, string supplier, IEnumerable<(Product P, decimal Qty, int ExpMonths)> items)
        {
            var r = new StockReceipt { Code = code, Date = date, Supplier = supplier, SupplierInvoiceNo = "HĐ" + rnd.Next(100000, 999999), CreatedById = uStock.Id, CreatedAt = date };
            foreach (var (p, qty, exp) in items)
                r.Lines.Add(new StockReceiptLine { ProductId = p.Id, Quantity = qty, UnitCost = p.CostPrice, Amount = qty * p.CostPrice,
                    LotNo = p.TrackExpiry ? $"L{date:yyMM}{rnd.Next(100, 999)}" : null, ExpiryDate = p.TrackExpiry ? date.AddMonths(exp) : null });
            r.Total = r.Lines.Sum(l => l.Amount);
            db.StockReceipts.Add(r);
            await db.SaveChangesAsync();
            await stock.PostReceiptAsync(r);
            foreach (var e in db.ChangeTracker.Entries<StockMove>().Where(e => e.State == EntityState.Added)) e.Entity.At = date.AddHours(9);
            r.PostedAt = date.AddHours(9);
            await db.SaveChangesAsync();
        }
        var d1 = today.AddDays(-75);
        await Receive($"{bc}-NK{d1:yyMM}-0001", d1, "Công ty TNHH Dược Thú y Sài Gòn",
            allProducts.Select(p => (p, p.Unit is "Viên" or "Cái" or "ml" or "Gói" ? 1500m : p.Kind == ProductKind.Vaccine ? 120m : p.Unit is "Lon" ? 120m : 40m,
                p.Kind == ProductKind.Vaccine ? 14 : 24)));
        var d2 = today.AddDays(-30);
        await Receive($"{bc}-NK{d2:yyMM}-0002", d2, "Công ty CP Thức ăn & Phụ kiện Thú cưng Việt",
            allProducts.Where(p => p.Kind is ProductKind.Food or ProductKind.Accessory).Select(p => (p, p.Unit is "Gói" or "Lon" ? 120m : 20m, 12)));
        // Lô vắc-xin dại sắp hết hạn (minh họa cảnh báo) + thuốc nhỏ tai gần hết hàng
        var d3 = today.AddDays(-8);
        await Receive($"{bc}-NK{d3:yyMM}-0003", d3, "Công ty TNHH Dược Thú y Sài Gòn", new[] { (vxRabies, 10m, 1), (melox, 5m, 2) });

        // ---------- Chuồng ----------
        Cage Cg(string code, string name, string size, decimal rate, bool iso = false)
        {
            var c = new Cage { Code = code, Name = name, Size = size, DailyRate = rate, IsIsolation = iso };
            db.Cages.Add(c);
            return c;
        }
        var cages = new List<Cage>
        {
            Cg("S01", "Phòng lưu trú 1 – ô 1", "Nhỏ", 120_000), Cg("S02", "Phòng lưu trú 1 – ô 2", "Nhỏ", 120_000), Cg("S03", "Phòng lưu trú 1 – ô 3", "Nhỏ", 120_000),
            Cg("S04", "Phòng lưu trú 1 – ô 4", "Nhỏ", 120_000), Cg("M01", "Phòng lưu trú 2 – ô 1", "Vừa", 160_000), Cg("M02", "Phòng lưu trú 2 – ô 2", "Vừa", 160_000),
            Cg("L01", "Chuồng lớn sân sau 1", "Lớn", 220_000), Cg("L02", "Chuồng lớn sân sau 2", "Lớn", 220_000),
            Cg("CL01", "Khu cách ly – ô 1", "Cách ly", 250_000, true), Cg("CL02", "Khu cách ly – ô 2", "Cách ly", 250_000, true)
        };
        await db.SaveChangesAsync();

        // ---------- Khách hàng & thú cưng ----------
        var species = await db.Species.Include(s => s.Breeds).ToListAsync();
        var dog = species.First(s => s.Name == "Chó");
        var cat = species.First(s => s.Name == "Mèo");
        var rabbit = species.First(s => s.Name == "Thỏ");
        var lastNames = new[] { "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý" };
        var middle = new[] { "Thị", "Văn", "Minh", "Ngọc", "Thanh", "Hoàng", "Quốc", "Gia", "Bảo", "Thu" };
        var first = new[] { "Anh", "Bích", "Cường", "Dung", "Giang", "Hà", "Hải", "Khoa", "Linh", "Long", "My", "Nam", "Nhung", "Phong", "Quân", "Tâm", "Thảo", "Trang", "Tuấn", "Vy", "Yến", "Khánh", "Duy", "Hạnh" };
        var streets = new[] { "Nguyễn Thị Thập", "Huỳnh Tấn Phát", "Lê Văn Lương", "Nguyễn Hữu Thọ", "Phạm Hùng", "Tôn Dật Tiên", "Nguyễn Lương Bằng", "Trần Xuân Soạn" };
        var dogNames = new[] { "Lu", "Milo", "Bơ", "Mực", "Vàng", "Cún", "Bông", "Lucky", "Rocky", "Bin", "Ki", "Mochi", "Sữa", "Đậu", "Tôm", "Max" };
        var catNames = new[] { "Mướp", "Miu", "Mimi", "Bé Na", "Tom", "Kem", "Gấu", "Su", "Mun", "Mây", "Luna", "Bơ Bơ" };
        var owners = new List<Owner>();
        var pets = new List<Pet>();
        for (var i = 0; i < 32; i++)
        {
            var o = new Owner
            {
                Code = $"KH{i + 1:00000}", FullName = $"{lastNames[i % lastNames.Length]} {middle[rnd.Next(middle.Length)]} {first[(i * 7) % first.Length]}",
                Phone = "09" + (10_000_000 + i * 2_713_517 % 89_999_999).ToString("00000000"), Address = $"{rnd.Next(1, 500)} {streets[i % streets.Length]}, Quận 7, TP. HCM",
                CreatedAt = today.AddDays(-rnd.Next(70, 400))
            };
            if (i % 3 == 0) o.Email = $"khachhang{i + 1}@gmail.com";
            if (i == 4) o.Note = "Khách thân thiết – ưu tiên giảm 10% dịch vụ spa";
            db.Owners.Add(o);
            owners.Add(o);
            var count = i % 5 == 0 ? 3 : i % 3 == 0 ? 2 : 1;
            for (var k = 0; k < count; k++)
            {
                var sp = (i + k) % 4 == 3 ? cat : (i + k) % 9 == 8 ? rabbit : (i + k) % 2 == 0 ? dog : cat;
                var breed = sp.Breeds[rnd.Next(sp.Breeds.Count)];
                var birth = today.AddMonths(-rnd.Next(3, 130)).AddDays(-rnd.Next(0, 28));
                var p = new Pet
                {
                    Code = $"TC{pets.Count + 1:00000}",
                    Name = sp == dog ? dogNames[(i + k * 5) % dogNames.Length] : sp == cat ? catNames[(i + k * 3) % catNames.Length] : "Bunny",
                    Owner = o, SpeciesId = sp.Id, BreedId = breed.Id, Gender = rnd.Next(2) == 0 ? PetGender.Male : PetGender.Female, BirthDate = birth,
                    WeightKg = sp == dog ? Math.Round((decimal)(rnd.NextDouble() * 25 + 2), 1) : sp == cat ? Math.Round((decimal)(rnd.NextDouble() * 4 + 2), 1) : 1.8m,
                    Color = new[] { "Vàng", "Đen", "Trắng", "Nâu", "Tam thể", "Xám", "Vàng trắng" }[rnd.Next(7)],
                    Neutered = rnd.Next(3) == 0, CreatedAt = o.CreatedAt
                };
                if (rnd.Next(6) == 0) p.Microchip = "9000" + rnd.Next(10000000, 99999999) + rnd.Next(100, 999);
                if (pets.Count % 11 == 4) p.Allergies = "Dị ứng kháng sinh nhóm Penicillin";
                db.Pets.Add(p);
                pets.Add(p);
            }
        }
        await db.SaveChangesAsync();
        bool IsDog(Pet p) => p.SpeciesId == dog.Id;
        bool IsCat(Pet p) => p.SpeciesId == cat.Id;

        // ---------- Đánh số chứng từ ----------
        var seq = new Dictionary<string, int>();
        string Code(string prefix, DateTime at)
        {
            var head = $"{bc}-{prefix}{at:yyMM}-";
            seq[head] = seq.GetValueOrDefault(head) + 1;
            return head + seq[head].ToString("0000");
        }
        foreach (var r in await db.StockReceipts.ToListAsync())
        {
            var cut = r.Code.LastIndexOf('-') + 1;
            seq[r.Code[..cut]] = Math.Max(seq.GetValueOrDefault(r.Code[..cut]), int.Parse(r.Code[cut..]));
        }

        // ---------- Lượt khám + hóa đơn ----------
        var diagnoses = new (string Reason, string Symptoms, string Exam, string Diagnosis, string Treatment, ServiceItem[] Services, (Product P, decimal Q, string Dose, int Days)[] Meds)[]
        {
            ("Bỏ ăn, nôn ói", "Nôn 3 lần từ hôm qua, bỏ ăn, đi phân lỏng", "Niêm mạc hơi nhợt, bụng chướng nhẹ, mất nước ~5%", "Viêm dạ dày – ruột cấp",
                "Truyền dịch bù nước, kháng sinh, men tiêu hóa; ăn cháo loãng 2 ngày", new[] { sExam, sTest, sFluid }, new[] { (metro, 10m, "Uống 1/2 viên x 2 lần/ngày", 5), (biolac, 6m, "1 gói x 2 lần/ngày", 3), (ringer, 1m, "Truyền tĩnh mạch", 1) }),
            ("Ngứa, rụng lông", "Gãi nhiều vùng lưng và tai, rụng lông từng mảng", "Da đỏ, có vảy gàu, soi thấy nấm", "Viêm da do nấm kết hợp ký sinh trùng",
                "Tắm thuốc 2 lần/tuần, nhỏ gáy trị ký sinh trùng", new[] { sExam, sSkin }, new[] { (pred, 10m, "Uống 1/2 viên/ngày", 5), (nexgard, 1m, "Uống 1 viên", 1) }),
            ("Lắc đầu, gãi tai", "Tai có mùi hôi, dịch nâu", "Ống tai đỏ, nhiều ráy tai", "Viêm tai ngoài",
                "Vệ sinh tai, nhỏ thuốc 7 ngày", new[] { sExam, sEar }, new[] { (otomax, 1m, "Nhỏ 4 giọt x 2 lần/ngày", 7) }),
            ("Ho, sổ mũi", "Ho khan nhiều về đêm, chảy nước mũi trong", "Nhiệt độ 39.4°C, phổi có tiếng rít nhẹ", "Viêm đường hô hấp trên",
                "Kháng sinh 7 ngày, giữ ấm", new[] { sExam, sInject }, new[] { (amox, 14m, "Uống 1 viên x 2 lần/ngày", 7), (enro, 2m, "Tiêm dưới da", 1) }),
            ("Đi khập khiễng", "Chân sau phải đi cà nhắc sau khi chạy nhảy", "Đau khi gập khớp gối, không sưng", "Bong gân khớp gối",
                "Hạn chế vận động 2 tuần, giảm đau", new[] { sExam, sXray }, new[] { (melox, 1m, "Uống 0.1ml/kg/ngày", 7) }),
            ("Khám sức khỏe định kỳ", "Không có biểu hiện bất thường", "Thể trạng tốt, răng có cao răng nhẹ", "Sức khỏe bình thường",
                "Tẩy giun định kỳ, theo dõi cân nặng", new[] { sExam, sBlood }, new[] { (drontal, 1m, "Uống 1 viên", 1) }),
            ("Tiểu khó, tiểu ra máu", "Đi tiểu nhiều lần, ít nước tiểu, có máu", "Bàng quang căng, đau khi sờ", "Viêm bàng quang",
                "Kháng sinh, uống nhiều nước, thức ăn hỗ trợ tiết niệu", new[] { sExam, sUltra, sBio }, new[] { (ceph, 14m, "Uống 1 viên x 2 lần/ngày", 7) }),
            ("Bị chó khác cắn", "Vết thương vùng cổ, chảy máu", "Vết rách 2cm, sạch, không tổn thương sâu", "Vết thương phần mềm",
                "Khâu vết thương, kháng sinh, đeo loa chống liếm", new[] { sExam, sSuture }, new[] { (amox, 10m, "Uống 1 viên x 2 lần/ngày", 5), (cone, 1m, "Đeo 7 ngày", 7), (gauze, 2m, "Thay băng hằng ngày", 3) })
        };
        var invoices = new List<Invoice>();

        async Task<Invoice> Bill(DateTime at, Owner? owner, Pet? pet, int? visitId, int? admId, AppUser? vet, IEnumerable<InvoiceLine> lines, decimal discount, decimal payRatio, PaymentMethod method, string? customerName = null)
        {
            var inv = new Invoice { Code = Code("HD", at), Date = at, Owner = owner, Pet = pet, VisitId = visitId, AdmissionId = admId, CreatedById = uCashier.Id, CreatedAt = at, CustomerName = customerName };
            foreach (var l in lines) { l.VetId ??= vet?.Id; inv.Lines.Add(l); }
            inv.Discount = discount;
            inv.Recalc();
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
            if (payRatio >= 0)
            {
                var products = await db.Products.ToDictionaryAsync(p => p.Id);
                foreach (var l in inv.Lines.Where(l => l.ProductId.HasValue))
                    l.CostAmount = await stock.IssueAsync(products[l.ProductId!.Value], l.Quantity, StockMoveType.Sale, nameof(Invoice), inv.Id, inv.Code, customerName ?? owner?.FullName);
                foreach (var e in db.ChangeTracker.Entries<StockMove>().Where(e => e.State == EntityState.Added)) e.Entity.At = at;
                inv.IssuedAt = at;
                inv.Status = InvoiceStatus.Unpaid;
                var pay = Math.Round(inv.Total * payRatio / 1000m, 0) * 1000m;
                if (payRatio >= 1) pay = inv.Total;
                if (pay > 0)
                {
                    inv.Payments.Add(new Payment { Amount = pay, Method = method, At = at.AddMinutes(10), UserId = uCashier.Id });
                    inv.Paid = pay;
                }
                InvoiceService.UpdateStatus(inv);
                await db.SaveChangesAsync();
            }
            invoices.Add(inv);
            return inv;
        }

        InvoiceLine SvcLine(ServiceItem s, decimal q = 1) => new() { Kind = InvoiceLineKind.Service, ServiceItemId = s.Id, Description = s.Name, Unit = s.Unit, Quantity = q, UnitPrice = s.Price };
        InvoiceLine PrdLine(Product p, decimal q, string? desc = null) => new() { Kind = InvoiceLineKind.Product, ProductId = p.Id, Description = desc ?? p.Name, Unit = p.Unit, Quantity = q, UnitPrice = p.SalePrice };

        async Task<Visit> MakeVisit(DateTime at, Pet pet, AppUser vet, int dx, VisitStatus status, bool bill, decimal payRatio = 1, int? apptId = null)
        {
            var d = diagnoses[dx];
            var v = new Visit
            {
                Code = Code("PK", at), PetId = pet.Id, OwnerId = pet.OwnerId, VetId = vet.Id, VisitAt = at, Status = status, Reason = d.Reason, AppointmentId = apptId,
                CreatedById = uReception.Id, CreatedAt = at,
                WeightKg = pet.WeightKg, Temperature = 38.2m + (decimal)rnd.Next(0, 15) / 10m, HeartRate = IsCat(pet) ? rnd.Next(140, 200) : rnd.Next(80, 140), RespiratoryRate = rnd.Next(18, 36)
            };
            if (status != VisitStatus.Waiting)
            {
                v.StartedAt = at.AddMinutes(10);
                v.Symptoms = d.Symptoms; v.Examination = d.Exam; v.Diagnosis = d.Diagnosis; v.Treatment = d.Treatment;
                v.Advice = "Theo dõi ăn uống, tái khám nếu không cải thiện sau 3 ngày.";
                foreach (var s in d.Services) v.Services.Add(new VisitService { ServiceItemId = s.Id, Quantity = 1, UnitPrice = s.Price });
                foreach (var m in d.Meds) v.Medicines.Add(new VisitMedicine { ProductId = m.P.Id, Quantity = m.Q, UnitPrice = m.P.SalePrice, Dosage = m.Dose, Days = m.Days });
            }
            if (status == VisitStatus.Completed)
            {
                v.CompletedAt = at.AddMinutes(40);
                if (dx is 0 or 3 or 6) v.FollowUpDate = at.Date.AddDays(5);
            }
            db.Visits.Add(v);
            await db.SaveChangesAsync();
            if (bill)
            {
                var lines = d.Services.Select(s => SvcLine(s)).Concat(d.Meds.Select(m => PrdLine(m.P, m.Q))).ToList();
                var inv = await Bill(at.AddMinutes(45), pet.Owner, pet, v.Id, null, vet, lines, 0, payRatio, rnd.Next(3) == 0 ? PaymentMethod.Transfer : PaymentMethod.Cash);
                v.InvoiceId = inv.Id;
                await db.SaveChangesAsync();
            }
            return v;
        }

        async Task VaccinateVisit(DateTime at, Pet pet, AppUser vet, int dose, bool bill = true, bool remindDone = true)
        {
            var vaccine = IsCat(pet) ? vxCat : dose >= 3 ? vxRabies : (pet.Id % 2 == 0 ? vx7 : vx5);
            var v = new Visit
            {
                Code = Code("PK", at), PetId = pet.Id, OwnerId = pet.OwnerId, VetId = vet.Id, VisitAt = at, Status = VisitStatus.Completed, Reason = $"Tiêm phòng mũi {dose}",
                WeightKg = pet.WeightKg, Temperature = 38.5m, Examination = "Thể trạng tốt, đủ điều kiện tiêm", Diagnosis = "Khỏe mạnh – tiêm phòng",
                StartedAt = at.AddMinutes(5), CompletedAt = at.AddMinutes(20), CreatedById = uReception.Id, CreatedAt = at
            };
            v.Services.Add(new VisitService { ServiceItemId = sVaccine.Id, Quantity = 1, UnitPrice = sVaccine.Price });
            db.Visits.Add(v);
            await db.SaveChangesAsync();
            db.Vaccinations.Add(new Vaccination
            {
                PetId = pet.Id, ProductId = vaccine.Id, VaccineName = vaccine.Name, DoseNumber = dose, Date = at.Date, VetId = vet.Id, VisitId = v.Id,
                NextDueDate = at.Date.AddDays(vaccine.BoosterDays ?? 365), LotNo = $"L{at:yyMM}{rnd.Next(100, 999)}", ReminderDone = remindDone, CreatedAt = at
            });
            if (bill)
            {
                var inv = await Bill(at.AddMinutes(25), pet.Owner, pet, v.Id, null, vet,
                    new[] { SvcLine(sVaccine), PrdLine(vaccine, 1, $"{vaccine.Name} (mũi {dose})") }, 0, 1, PaymentMethod.Cash);
                v.InvoiceId = inv.Id;
            }
            await db.SaveChangesAsync();
        }

        // 60 ngày lịch sử
        for (var day = 60; day >= 1; day--)
        {
            var date = today.AddDays(-day);
            var n = date.DayOfWeek == DayOfWeek.Sunday ? 2 : rnd.Next(3, 7);
            for (var k = 0; k < n; k++)
            {
                var at = date.AddHours(8 + k * 1.5 + rnd.Next(0, 2) * 0.5);
                var pet = pets[rnd.Next(pets.Count)];
                var vet = vets[rnd.Next(vets.Length)];
                var kind = rnd.Next(10);
                if (kind < 6)
                {
                    var dx = rnd.Next(diagnoses.Length);
                    if (!IsDog(pet) && dx == 7) dx = 2;
                    await MakeVisit(at, pet, vet, dx, VisitStatus.Completed, true, day <= 3 && k == 0 ? 0.5m : 1);
                }
                else if (kind < 8) await VaccinateVisit(at, pet, vet, rnd.Next(1, 4));
                else
                {
                    // Spa / grooming hoặc bán lẻ
                    var lines = new List<InvoiceLine>();
                    if (IsDog(pet)) { lines.Add(SvcLine((pet.WeightKg ?? 5) < 10 ? sBathS : sBathL)); if (rnd.Next(2) == 0) lines.Add(SvcLine(sCut)); }
                    else lines.Add(SvcLine(sBathCat));
                    lines.Add(SvcLine(sNail));
                    if (rnd.Next(2) == 0) lines.Add(PrdLine(IsDog(pet) ? rcMini : rcKitten, 1));
                    if (rnd.Next(3) == 0) lines.Add(PrdLine(IsCat(pet) ? litter : shampoo, 1));
                    await Bill(at, pet.Owner, pet, null, null, null, lines, pet.Owner!.Note != null ? Math.Round(lines.Where(l => l.Kind == InvoiceLineKind.Service).Sum(l => l.Quantity * l.UnitPrice) * 0.1m, 0) : 0, 1,
                        rnd.Next(2) == 0 ? PaymentMethod.Transfer : PaymentMethod.Cash);
                }
            }
            // Khách lẻ mua hàng
            if (day % 4 == 0)
                await Bill(date.AddHours(17), null, null, null, null, null, new[] { PrdLine(pate, rnd.Next(2, 7)), PrdLine(drontalCat, 2) }, 0, 1, PaymentMethod.Cash, "Khách lẻ");
        }

        // Phẫu thuật triệt sản (đã xong) cho vài thú cưng
        foreach (var pet in pets.Where(p => !p.Neutered && p.SpeciesId != rabbit.Id).Take(4))
        {
            var at = today.AddDays(-rnd.Next(5, 40)).AddHours(9);
            var surgery = IsCat(pet) ? (pet.Gender == PetGender.Female ? sNeuterCatF : sNeuterCatM) : sNeuterDogM;
            var v = new Visit
            {
                Code = Code("PK", at), PetId = pet.Id, OwnerId = pet.OwnerId, VetId = uManager.Id, VisitAt = at, Status = VisitStatus.Completed, Reason = "Triệt sản theo yêu cầu",
                Examination = "Xét nghiệm máu trước mổ trong giới hạn bình thường", Diagnosis = "Đủ điều kiện phẫu thuật", Treatment = surgery.Name + ", gây mê hô hấp",
                Advice = "Đeo loa chống liếm 7 ngày, cắt chỉ sau 10 ngày", FollowUpDate = at.Date.AddDays(10), WeightKg = pet.WeightKg, Temperature = 38.4m,
                StartedAt = at.AddMinutes(10), CompletedAt = at.AddHours(2), CreatedById = uReception.Id, CreatedAt = at
            };
            v.Services.Add(new VisitService { ServiceItemId = sBlood.Id, Quantity = 1, UnitPrice = sBlood.Price });
            v.Services.Add(new VisitService { ServiceItemId = surgery.Id, Quantity = 1, UnitPrice = surgery.Price });
            v.Medicines.Add(new VisitMedicine { ProductId = amox.Id, Quantity = 10, UnitPrice = amox.SalePrice, Dosage = "Uống 1 viên x 2 lần/ngày", Days = 5 });
            v.Medicines.Add(new VisitMedicine { ProductId = cone.Id, Quantity = 1, UnitPrice = cone.SalePrice, Dosage = "Đeo 7–10 ngày" });
            db.Visits.Add(v);
            pet.Neutered = true;
            await db.SaveChangesAsync();
            var inv = await Bill(at.AddHours(3), pet.Owner, pet, v.Id, null, uManager,
                new[] { SvcLine(sBlood), SvcLine(surgery), PrdLine(amox, 10), PrdLine(cone, 1) }, 0, 1, PaymentMethod.Transfer);
            v.InvoiceId = inv.Id;
            await db.SaveChangesAsync();
        }

        // ---------- Nhắc tiêm: mũi tiếp theo sắp đến hạn / quá hạn ----------
        var vaccinePets = pets.Where(p => p.SpeciesId != rabbit.Id).Skip(5).Take(6).ToList();
        for (var i = 0; i < vaccinePets.Count; i++)
        {
            var pet = vaccinePets[i];
            var vaccine = IsCat(pet) ? vxCat : vx7;
            // ngày tiêm sao cho mũi kế tiếp rơi vào từ 3 ngày trước đến 6 ngày tới
            var due = today.AddDays(i - 3);
            var at = due.AddDays(-(vaccine.BoosterDays ?? 21)).AddHours(10);
            await VaccinateVisit(at, pet, vets[i % vets.Length], 1, true, false);
        }

        // ---------- Lịch hẹn ----------
        Appointment Appt(DateTime at, Pet pet, AppUser? vet, ServiceItem? svc, string reason, AppointmentStatus st, int minutes = 30)
        {
            var a = new Appointment
            {
                Code = Code("LH", at), OwnerId = pet.OwnerId, PetId = pet.Id, VetId = vet?.Id, ServiceItemId = svc?.Id, StartAt = at, DurationMinutes = minutes,
                Reason = reason, Status = st, CreatedById = uReception.Id, CreatedAt = at.AddDays(-2)
            };
            db.Appointments.Add(a);
            return a;
        }
        var p0 = pets[2]; var p1 = pets[7]; var p2 = pets[12]; var p3 = pets[18]; var p4 = pets[23]; var p5 = pets[29]; var p6 = pets[33]; var p7 = pets[40];
        var aDone = Appt(today.AddHours(8), p0, vAn, sExam, "Bỏ ăn 2 ngày", AppointmentStatus.Completed);
        var aDone2 = Appt(today.AddHours(8.5), p1, vBinh, sVaccine, "Tiêm phòng mũi 2", AppointmentStatus.Completed, 20);
        var aIn = Appt(today.AddHours(9.5), p2, vAn, sExam, "Ngứa, rụng lông", AppointmentStatus.CheckedIn);
        var aWait = Appt(today.AddHours(10), p3, vChau, sRecheck, "Tái khám viêm tai", AppointmentStatus.CheckedIn, 15);
        var aWait2 = Appt(today.AddHours(10.5), p4, null, sExam, "Ho, sổ mũi", AppointmentStatus.CheckedIn);
        Appt(today.AddHours(14), p5, vBinh, sBathS, "Tắm + cắt tỉa lông", AppointmentStatus.Confirmed, 90);
        Appt(today.AddHours(15), p6, vChau, sDental, "Cạo vôi răng", AppointmentStatus.Booked, 60);
        Appt(today.AddHours(16.5), p7, vAn, sVaccine, "Tiêm dại nhắc lại", AppointmentStatus.Booked, 20);
        Appt(today.AddDays(-1).AddHours(16), pets[3], vBinh, sExam, "Khám da", AppointmentStatus.NoShow);
        for (var i = 1; i <= 5; i++)
        {
            Appt(today.AddDays(i).AddHours(9), pets[(i * 5) % pets.Count], vets[i % 3], i % 2 == 0 ? sVaccine : sExam, i % 2 == 0 ? "Tiêm phòng nhắc lại" : "Khám định kỳ", AppointmentStatus.Booked);
            Appt(today.AddDays(i).AddHours(14.5), pets[(i * 7 + 3) % pets.Count], vets[(i + 1) % 3], i % 3 == 0 ? sNeuterCatM : sBathCat, i % 3 == 0 ? "Triệt sản" : "Spa", AppointmentStatus.Confirmed, 60);
        }
        await db.SaveChangesAsync();

        // Lượt khám hôm nay từ lịch hẹn
        var vt0 = await MakeVisit(today.AddHours(8.1), p0, vAn, 0, VisitStatus.Completed, true, 0, aDone.Id);
        aDone.VisitId = vt0.Id;
        await VaccinateVisit(today.AddHours(8.6), p1, vBinh, 2, false, true);
        var vt1 = await db.Visits.OrderByDescending(v => v.Id).FirstAsync();
        vt1.AppointmentId = aDone2.Id; aDone2.VisitId = vt1.Id;
        var inv1 = new Invoice
        {
            Code = Code("HD", today), Date = today.AddHours(8.9), Owner = p1.Owner, Pet = p1, VisitId = vt1.Id, CreatedById = uReception.Id, Note = $"Phiếu khám {vt1.Code}"
        };
        var lastVacc = await db.Vaccinations.FirstAsync(x => x.VisitId == vt1.Id);
        var vaccProduct = allProducts.First(p => p.Id == lastVacc.ProductId);
        inv1.Lines.Add(SvcLine(sVaccine));
        inv1.Lines.Add(PrdLine(vaccProduct, 1, $"{vaccProduct.Name} (mũi 2)"));
        inv1.Recalc();
        db.Invoices.Add(inv1);
        await db.SaveChangesAsync();
        vt1.InvoiceId = inv1.Id;
        var vt2 = await MakeVisit(today.AddHours(9.6), p2, vAn, 1, VisitStatus.InProgress, false, 1, aIn.Id);
        aIn.VisitId = vt2.Id;
        var vt3 = await MakeVisit(today.AddHours(10.1), p3, vChau, 2, VisitStatus.Waiting, false, 1, aWait.Id);
        aWait.VisitId = vt3.Id;
        var vt4 = await MakeVisit(today.AddHours(10.6), p4, vBinh, 3, VisitStatus.Waiting, false, 1, aWait2.Id);
        aWait2.VisitId = vt4.Id;
        await db.SaveChangesAsync();

        // ---------- Nội trú / lưu chuồng ----------
        var hp = pets[9];
        var admVisit = await MakeVisit(today.AddDays(-2).AddHours(11), hp, vChau, 0, VisitStatus.Completed, false);
        var adm1 = new Admission
        {
            Code = Code("NT", today.AddDays(-2)), Kind = AdmissionKind.Hospitalization, PetId = hp.Id, OwnerId = hp.OwnerId, CageId = cages[8].Id, VetId = vChau.Id,
            VisitId = admVisit.Id, AdmitAt = today.AddDays(-2).AddHours(12), ExpectedDischarge = today.AddDays(2), Reason = "Viêm dạ dày – ruột cấp, mất nước, theo dõi Parvo",
            CareNote = "Cách ly, truyền dịch 2 lần/ngày, cho ăn cháo loãng", DailyRate = cages[8].DailyRate, CreatedById = vChau.Id, CreatedAt = today.AddDays(-2).AddHours(12)
        };
        adm1.Notes.Add(new AdmissionNote { At = today.AddDays(-2).AddHours(18), Temperature = 39.6m, WeightKg = hp.WeightKg, Appetite = "Bỏ ăn", Note = "Còn nôn 1 lần, phân lỏng", Treatment = "Truyền dịch 250ml, tiêm chống nôn", UserId = vChau.Id });
        adm1.Notes.Add(new AdmissionNote { At = today.AddDays(-1).AddHours(8), Temperature = 39.1m, Appetite = "Ăn ít", Note = "Không nôn, phân sệt", Treatment = "Truyền dịch, kháng sinh", UserId = vAn.Id });
        adm1.Notes.Add(new AdmissionNote { At = today.AddHours(8), Temperature = 38.7m, Appetite = "Ăn được 1/2 khẩu phần", Note = "Tỉnh táo, đi lại bình thường", Treatment = "Kháng sinh đường uống", UserId = vChau.Id });
        db.Admissions.Add(adm1);
        var bp = pets[15];
        var adm2 = new Admission
        {
            Code = Code("NT", today.AddDays(-3)), Kind = AdmissionKind.Boarding, PetId = bp.Id, OwnerId = bp.OwnerId, CageId = cages[6].Id,
            AdmitAt = today.AddDays(-3).AddHours(17), ExpectedDischarge = today.AddDays(1), Reason = "Chủ đi công tác",
            CareNote = "Gửi kèm 1 túi thức ăn, đồ chơi; dắt đi dạo 2 lần/ngày", DailyRate = cages[6].DailyRate, CreatedById = uReception.Id, CreatedAt = today.AddDays(-3).AddHours(17)
        };
        db.Admissions.Add(adm2);
        // Đã xuất
        for (var i = 0; i < 3; i++)
        {
            var pet = pets[20 + i * 4];
            var cage = cages[i];
            var admitAt = today.AddDays(-25 + i * 6).AddHours(9);
            var a = new Admission
            {
                Code = Code("NT", admitAt), Kind = i == 1 ? AdmissionKind.Hospitalization : AdmissionKind.Boarding, PetId = pet.Id, OwnerId = pet.OwnerId, CageId = cage.Id,
                VetId = i == 1 ? vBinh.Id : null, AdmitAt = admitAt, ExpectedDischarge = admitAt.Date.AddDays(3), DischargeAt = admitAt.AddDays(3).AddHours(2),
                Reason = i == 1 ? "Theo dõi sau phẫu thuật" : "Gửi giữ dịp nghỉ lễ", DailyRate = cage.DailyRate, Status = AdmissionStatus.Discharged,
                DischargeNote = "Khỏe mạnh, ăn uống tốt", CreatedById = uReception.Id, CreatedAt = admitAt
            };
            db.Admissions.Add(a);
            await db.SaveChangesAsync();
            var inv = await Bill(a.DischargeAt!.Value, pet.Owner, pet, null, a.Id, null,
                new[] { new InvoiceLine { Kind = InvoiceLineKind.Other, Description = $"{a.Kind.GetDisplayName()} – chuồng {cage.Code}", Unit = "Ngày", Quantity = a.BillableDays(), UnitPrice = a.DailyRate, VetId = a.VetId } },
                0, 1, PaymentMethod.Cash);
            a.InvoiceId = inv.Id;
        }
        await db.SaveChangesAsync();

        // ---------- Thông tin phòng khám ----------
        foreach (var (key, _, def) in SettingsService.All)
            if (!await db.SystemSettings.AnyAsync(s => s.Key == key)) db.SystemSettings.Add(new SystemSetting { Key = key, Value = def });
        await db.SaveChangesAsync();
    }
}
