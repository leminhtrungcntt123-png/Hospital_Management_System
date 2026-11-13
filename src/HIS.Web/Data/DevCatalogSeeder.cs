using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.Web.Data;

// Danh mục chuyên khoa + bác sĩ mẫu cho cổng bệnh nhân. Idempotent: chạy lại không tạo trùng.
public static class DevCatalogSeeder
{
    private sealed record Dept(string Name, string Description, string RoomPrefix);

    private sealed record Doc(
        string Username, string Name, string Dept, string Specialty, string Title, short Years,
        string? Avatar, bool IsPublic, int Order, string ShortBio, string? Biography = null);

    private static readonly Dept[] Departments =
    [
        new("Ngoại tổng quát", "Khám ngoại khoa, xử trí chấn thương và tư vấn phẫu thuật.", "P21"),
        new("Tim mạch", "Khám, theo dõi bệnh lý tim và hệ tuần hoàn: tăng huyết áp, rối loạn nhịp, suy tim.", "P3"),
        new("Nhi khoa", "Chăm sóc sức khoẻ trẻ em từ sơ sinh đến 16 tuổi, theo dõi tăng trưởng và tiêm chủng.", "P4"),
        new("Sản phụ khoa", "Khám phụ khoa, quản lý thai kỳ và chăm sóc sức khoẻ phụ nữ qua từng giai đoạn.", "P5"),
        new("Tai Mũi Họng", "Khám và điều trị bệnh lý tai, mũi xoang, họng, thanh quản và thính giác.", "P6"),
        new("Da liễu", "Khám bệnh lý da, tóc, móng; tư vấn chăm sóc da chuyên khoa.", "P7"),
        new("Mắt", "Khám thị lực, đo khúc xạ, theo dõi bệnh lý mắt và sức khoẻ thị giác.", "P8"),
        new("Răng Hàm Mặt", "Khám răng miệng, nha chu, chỉnh nha và bệnh lý vùng hàm mặt.", "P9"),
        new("Phục hồi chức năng", "Phục hồi vận động sau chấn thương, tai biến và các bệnh lý cơ xương.", "P10"),
        new("Thần kinh", "Khám đau đầu, chóng mặt, mất ngủ, tê bì và các bệnh lý thần kinh.", "P11"),
        new("Tiêu hoá", "Khám bệnh lý dạ dày, đại tràng, gan mật và rối loạn tiêu hoá.", "P12"),
        new("Cơ xương khớp", "Khám đau khớp, thoái hoá, loãng xương và bệnh lý cột sống.", "P13"),
        new("Hô hấp", "Khám ho kéo dài, hen phế quản, COPD và bệnh lý phổi.", "P14"),
    ];

    private static readonly Doc[] Doctors =
    [
        // Ngoại tổng quát
        new("bs_cuong", "BS. Phan Quốc Cường", "Ngoại tổng quát", "Ngoại chấn thương", "BS.CKII", 19, null, false, 100,
            "Khám ngoại khoa, xử trí chấn thương phần mềm và tư vấn phẫu thuật."),
        new("bs_dung", "BS. Vương Tiến Dũng", "Ngoại tổng quát", "Ngoại tiêu hoá", "BS.CKI", 9, null, false, 100,
            "Khám bệnh lý ngoại tiêu hoá và theo dõi sau phẫu thuật."),
        // Tim mạch
        new("bs_tuan", "BS. Đặng Anh Tuấn", "Tim mạch", "Sức khoẻ tim và hệ tuần hoàn", "TS.BS", 28, "doctor-tuan.jpg", true, 10,
            "Chuyên gia tim mạch với gần 30 năm theo dõi người bệnh tăng huyết áp và suy tim.",
            "Quan tâm xây dựng quá trình chăm sóc tim mạch có sự phối hợp giữa người bệnh, gia đình và các chuyên khoa liên quan. Định hướng chuyên môn gắn với việc theo dõi liên tục, trao đổi rõ ràng và chuẩn hoá thông tin trong hồ sơ khám."),
        new("bs_phuc", "BS. Trần Văn Phúc", "Tim mạch", "Rối loạn nhịp tim, tăng huyết áp", "ThS.BS", 13, "doctor-phuc.jpg", true, 11,
            "Theo dõi rối loạn nhịp và tăng huyết áp, chú trọng hướng dẫn lối sống cho người bệnh.",
            "Tập trung vào rối loạn nhịp tim và kiểm soát huyết áp lâu dài. Mỗi buổi khám đều dành thời gian hướng dẫn người bệnh tự theo dõi tại nhà và điều chỉnh chế độ sinh hoạt."),
        new("bs_hieu", "BS. Nguyễn Trung Hiếu", "Tim mạch", "Siêu âm tim", "BS.CKI", 7, null, false, 100,
            "Bác sĩ siêu âm tim và khám tim mạch tổng quát."),
        // Nhi khoa
        new("bs_khanh", "BS. Nguyễn Minh Khánh", "Nhi khoa", "Sức khoẻ và sự phát triển của trẻ", "BS.CKI", 9, "doctor-khanh.jpg", true, 2,
            "Khám nhi thân thiện, theo dõi tăng trưởng và phát triển của trẻ theo độ tuổi.",
            "Ưu tiên tạo không gian thăm khám thân thiện để trẻ và phụ huynh cảm thấy thoải mái khi trao đổi. Quan tâm việc theo dõi sự phát triển theo độ tuổi và xây dựng mối liên hệ rõ ràng giữa gia đình với đội ngũ chăm sóc."),
        new("bs_long", "BS. Bùi Hải Long", "Nhi khoa", "Chăm sóc sức khoẻ trẻ em", "BS.CKII", 20, "doctor-long.jpg", true, 12,
            "Hơn 20 năm khám nhi, đặc biệt các bệnh lý hô hấp và tiêu hoá ở trẻ nhỏ.",
            "Dành sự quan tâm cho quá trình theo dõi sức khoẻ trẻ em và những thay đổi qua từng giai đoạn phát triển. Coi trọng việc lắng nghe phụ huynh, ghi nhận thông tin có hệ thống và tạo sự gần gũi trong mỗi lần thăm khám."),
        new("bs_vy", "BS. Phan Tường Vy", "Nhi khoa", "Dinh dưỡng nhi", "BS", 5, null, false, 100,
            "Tư vấn dinh dưỡng và tiêm chủng cho trẻ."),
        // Sản phụ khoa
        new("bs_an", "BS. Trần Hoài An", "Sản phụ khoa", "Chăm sóc sức khoẻ phụ nữ", "BS.CKII", 21, "doctor-an.jpg", true, 3,
            "Đồng hành cùng sức khoẻ phụ nữ, quản lý thai kỳ và khám phụ khoa định kỳ.",
            "Đồng hành cùng sức khoẻ phụ nữ qua nhiều giai đoạn của cuộc sống, với phong cách tư vấn kín đáo và tôn trọng sự riêng tư. Đề cao việc giải thích thông tin rõ ràng để người đến khám chủ động trao đổi những điều còn băn khoăn."),
        new("bs_quynh", "BS. Võ Như Quỳnh", "Sản phụ khoa", "Sức khoẻ phụ nữ và chăm sóc thai kỳ", "ThS.BS", 16, "doctor-quynh.jpg", true, 13,
            "Theo dõi thai kỳ và tư vấn sức khoẻ sinh sản.",
            "Quan tâm xây dựng mối quan hệ tin cậy để phụ nữ cảm thấy an tâm khi chia sẻ thông tin sức khoẻ. Phong cách tư vấn chú trọng sự riêng tư, giải thích dễ hiểu và phối hợp theo dõi qua các lần hẹn chuyên khoa."),
        new("bs_lananh", "BS. Lê Lan Anh", "Sản phụ khoa", "Siêu âm sản khoa", "BS", 6, null, false, 100,
            "Siêu âm và khám thai định kỳ."),
        // Tai Mũi Họng
        new("bs_minh", "BS. Vũ Quang Minh", "Tai Mũi Họng", "Sức khoẻ tai, mũi, họng và thính giác", "ThS.BS", 15, "doctor-minh.jpg", true, 4,
            "Khám và nội soi tai mũi họng, theo dõi viêm xoang và thính lực.",
            "Tập trung vào lĩnh vực tai, mũi, họng và thính giác, kết hợp thăm khám với trao đổi về nhu cầu sinh hoạt hằng ngày. Chú trọng giải thích trình tự buổi khám và giúp người bệnh chuẩn bị đầy đủ thông tin trước các lần hẹn."),
        new("bs_viet", "BS. Lâm Quốc Việt", "Tai Mũi Họng", "Chăm sóc tai, mũi, họng và thính giác", "BS.CKI", 11, "doctor-viet.jpg", true, 14,
            "Điều trị viêm mũi dị ứng, viêm họng mạn và các bệnh lý tai.",
            "Chú trọng cách tiếp cận gần gũi, giúp người bệnh mô tả rõ những khó chịu ảnh hưởng đến công việc và sinh hoạt. Quan tâm việc lưu trữ thông tin thăm khám dễ theo dõi và duy trì trao đổi nhất quán giữa các lần tái khám."),
        // Da liễu
        new("bs_linh", "BS. Lê Phương Linh", "Da liễu", "Chăm sóc sức khoẻ da, tóc và móng", "ThS.BS", 8, "doctor-linh.jpg", true, 1,
            "Khám mụn, viêm da, nám và tư vấn chăm sóc da chuyên khoa.",
            "Quan tâm đến sức khoẻ làn da và trải nghiệm tư vấn riêng tư, nhẹ nhàng cho người đến khám. Chú trọng lắng nghe những thay đổi trong sinh hoạt, nhu cầu chăm sóc cá nhân và mong muốn của từng người bệnh."),
        new("bs_hanh", "BS. Đinh Mỹ Hạnh", "Da liễu", "Sức khoẻ làn da, tóc và da đầu", "BS.CKII", 25, "doctor-hanh.jpg", true, 15,
            "Chuyên gia da liễu với 25 năm kinh nghiệm về bệnh lý da và da đầu.",
            "Quan tâm đến việc đồng hành lâu dài cùng người bệnh trong theo dõi sức khoẻ da, tóc và da đầu. Phong cách tư vấn điềm tĩnh, lắng nghe trải nghiệm cá nhân và giúp người bệnh hiểu rõ nội dung hồ sơ chuyên khoa của mình."),
        // Mắt
        new("bs_ngoc", "BS. Phạm Bích Ngọc", "Mắt", "Chăm sóc mắt và sức khoẻ thị giác", "BS.CKI", 12, "doctor-ngoc.jpg", true, 16,
            "Khám khúc xạ, khô mắt và theo dõi bệnh lý đáy mắt.",
            "Chú trọng nhu cầu thị giác trong học tập, công việc và sinh hoạt của từng người đến khám. Phong cách tư vấn cẩn thận, dành thời gian giải thích nội dung thăm khám và trao đổi về quá trình theo dõi sức khoẻ đôi mắt."),
        new("bs_khoa", "BS. Trịnh Đăng Khoa", "Mắt", "Khúc xạ học", "BS", 4, null, false, 100,
            "Đo khúc xạ và khám mắt trẻ em."),
        // Răng Hàm Mặt
        new("bs_bao", "BS. Hoàng Gia Bảo", "Răng Hàm Mặt", "Sức khoẻ răng miệng và hàm mặt", "BS", 6, "doctor-bao.jpg", true, 17,
            "Khám răng tổng quát, điều trị sâu răng và nha chu.",
            "Hướng đến trải nghiệm thăm khám răng miệng nhẹ nhàng và dễ trao đổi, đặc biệt với người còn lo lắng khi đến phòng khám. Quan tâm việc trình bày rõ từng bước tiếp nhận và ghi nhận nhu cầu chăm sóc của người bệnh."),
        new("bs_trang", "BS. Ngô Thu Trang", "Răng Hàm Mặt", "Chỉnh nha và thẩm mỹ răng", "ThS.BS", 10, "doctor-trang.jpg", true, 18,
            "Tư vấn chỉnh nha, niềng răng và phục hình thẩm mỹ.",
            "Đồng hành cùng người bệnh trong các liệu trình chỉnh nha kéo dài, giải thích kỹ từng giai đoạn để người bệnh chủ động theo dõi và tái khám đúng hẹn."),
        // Phục hồi chức năng
        new("bs_thao", "BS. Đỗ Thanh Thảo", "Phục hồi chức năng", "Vận động và chức năng sinh hoạt", "ThS.BS", 10, "doctor-thao.jpg", true, 19,
            "Phục hồi vận động sau chấn thương, tai biến và phẫu thuật.",
            "Quan tâm đến khả năng vận động và mức độ độc lập trong sinh hoạt của người bệnh. Đề cao sự phối hợp với gia đình và đội ngũ chăm sóc để ghi nhận những thay đổi nhỏ trong suốt quá trình theo dõi phục hồi."),
        new("bs_son", "BS. Cao Văn Sơn", "Phục hồi chức năng", "Vật lý trị liệu", "BS", 8, null, false, 100,
            "Vật lý trị liệu và phục hồi đau cột sống."),
        // Thần kinh
        new("bs_thinh", "BS. Lý Đức Thịnh", "Thần kinh", "Đau đầu, chóng mặt, mất ngủ", "BS.CKII", 18, "doctor-thinh.jpg", true, 20,
            "Khám đau đầu mạn, rối loạn giấc ngủ và bệnh lý thần kinh ngoại biên.",
            "Kiên nhẫn khai thác diễn biến triệu chứng để tìm nguyên nhân đau đầu, chóng mặt kéo dài. Chú trọng hướng dẫn người bệnh ghi nhật ký triệu chứng giữa các lần tái khám."),
        new("bs_duy", "BS. Hồ Khánh Duy", "Thần kinh", "Thần kinh tổng quát", "BS", 6, null, false, 100,
            "Khám thần kinh tổng quát."),
        // Tiêu hoá
        new("bs_nhung", "BS. Mai Hồng Nhung", "Tiêu hoá", "Dạ dày, đại tràng, gan mật", "ThS.BS", 14, "doctor-nhung.jpg", true, 21,
            "Khám và theo dõi viêm dạ dày, hội chứng ruột kích thích, bệnh gan.",
            "Quan tâm đến mối liên hệ giữa thói quen ăn uống, sinh hoạt và các rối loạn tiêu hoá. Mỗi buổi khám đều kèm hướng dẫn chế độ ăn phù hợp cho từng người bệnh."),
        new("bs_tam", "BS. Đoàn Minh Tâm", "Tiêu hoá", "Nội soi tiêu hoá", "BS.CKI", 9, null, false, 100,
            "Nội soi dạ dày, đại tràng."),
        // Cơ xương khớp
        new("bs_quan", "BS. Tạ Minh Quân", "Cơ xương khớp", "Thoái hoá khớp, loãng xương", "BS.CKI", 12, "doctor-quan.jpg", true, 22,
            "Điều trị đau khớp, thoái hoá cột sống và loãng xương ở người lớn tuổi.",
            "Tập trung vào kiểm soát đau và duy trì khả năng vận động cho người bệnh cơ xương khớp mạn tính, phối hợp chặt chẽ với khoa Phục hồi chức năng."),
        new("bs_ha", "BS. Lương Thu Hà", "Cơ xương khớp", "Bệnh khớp viêm", "BS", 7, null, false, 100,
            "Khám gút, viêm khớp dạng thấp."),
        // Hô hấp
        new("bs_kiet", "BS. Châu Tuấn Kiệt", "Hô hấp", "Hen phế quản, COPD", "BS.CKI", 11, null, false, 100,
            "Khám ho kéo dài, hen và bệnh phổi tắc nghẽn mạn tính."),
        new("bs_yen", "BS. Kiều Hải Yến", "Hô hấp", "Hô hấp tổng quát", "BS", 5, null, false, 100,
            "Khám bệnh lý hô hấp thường gặp."),
    ];

    // Ảnh + hồ sơ cho 4 bác sĩ có sẵn tài khoản test.
    private static readonly Dictionary<string, (string Avatar, string Title, short Years, int Order, string Bio)> ExistingProfiles = new()
    {
        ["bs_nam"] = ("doctor-nam.jpg", "ThS.BS", 14, 5,
            "Chú trọng tiếp cận người bệnh một cách toàn diện, kết hợp khai thác bệnh sử với theo dõi diễn biến sức khoẻ. Phong cách tư vấn rõ ràng, dành thời gian giải thích kế hoạch chăm sóc và trao đổi cùng người bệnh."),
        ["head_noi"] = ("doctor-hoa.jpg", "BS.CKII", 22, 6,
            "Trưởng khoa Nội tổng quát. Quan tâm đến việc đồng hành lâu dài cùng người bệnh mạn tính, trình bày thông tin dễ hiểu và lắng nghe những băn khoăn của từng người."),
        ["bs_huy"] = ("doctor-hung.jpg", "BS.CKI", 11, 7,
            "Định hướng chuyên môn vào khám nội tổng quát và theo dõi bệnh lý chuyển hoá. Đề cao việc giải thích từng bước tiếp nhận và duy trì liên hệ giữa các lần tái khám."),
        ["bs_thu"] = ("doctor-mai.jpg", "TS.BS", 12, 8,
            "Theo dõi bệnh lý tim mạch trong khoa Nội: tăng huyết áp, rối loạn mỡ máu, bệnh mạch vành ổn định. Chú trọng giải thích kế hoạch điều trị và hướng dẫn người bệnh tự theo dõi tại nhà."),
    };

    public static async Task SeedAsync(AppDbContext db, string passwordHash, int reviewerAccountId)
    {
        // 1. Khoa + phòng (mỗi bác sĩ một phòng để không trùng buồng cùng ca).
        var deptIds = new Dictionary<string, int>();
        foreach (var d in Departments)
        {
            var dept = await db.Departments.FirstOrDefaultAsync(x => x.Name == d.Name);
            if (dept == null)
            {
                dept = new Department { Name = d.Name, Description = d.Description };
                db.Departments.Add(dept);
                await db.SaveChangesAsync();
            }
            deptIds[d.Name] = dept.Id;
        }

        // 2. Tài khoản + bác sĩ + phòng riêng.
        var roomOf = new Dictionary<int, int>(); // doctorId -> roomId
        var perDeptIndex = new Dictionary<string, int>();
        var license = 100;
        foreach (var d in Doctors)
        {
            license++;
            var dept = Departments.First(x => x.Name == d.Dept);
            var n = perDeptIndex[d.Dept] = perDeptIndex.GetValueOrDefault(d.Dept) + 1;

            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Username == d.Username);
            if (acc == null)
            {
                acc = new Account { Username = d.Username, Email = d.Username + "@his.local", PasswordHash = passwordHash, RoleId = (int)RoleId.Doctor, IsActive = true };
                db.Accounts.Add(acc);
                await db.SaveChangesAsync();
            }

            var doc = await db.Doctors.FirstOrDefaultAsync(x => x.AccountId == acc.Id);
            if (doc == null)
            {
                doc = new Doctor
                {
                    AccountId = acc.Id,
                    DepartmentId = deptIds[d.Dept],
                    FullName = d.Name,
                    Specialty = d.Specialty,
                    AcademicTitle = d.Title,
                    LicenseNumber = $"CCHN-{license:000}",
                    YearsOfExperience = d.Years,
                    AvatarUrl = d.Avatar,
                    IsPublic = d.IsPublic,
                    IsFeatured = d.IsPublic && d.Order < 10,
                    DisplayOrder = d.Order,
                    ShortBio = d.ShortBio,
                    Biography = d.Biography ?? d.ShortBio,
                    EmploymentType = EmploymentType.FullTime,
                    MinShiftsPerWeek = 4,
                    MaxShiftsPerWeek = 10
                };
                db.Doctors.Add(doc);
                await db.SaveChangesAsync();
            }

            var roomNumber = $"{dept.RoomPrefix}{n:00}";
            var room = await db.ClinicRooms.FirstOrDefaultAsync(r => r.RoomNumber == roomNumber);
            if (room == null)
            {
                room = new ClinicRoom { DepartmentId = deptIds[d.Dept], RoomName = $"Phòng khám {d.Dept} {n}", RoomNumber = roomNumber };
                db.ClinicRooms.Add(room);
                await db.SaveChangesAsync();
            }
            roomOf[doc.Id] = room.Id;
        }

        // 3. Bổ sung ảnh/hồ sơ cho bác sĩ có sẵn và cho họ phòng riêng.
        foreach (var (username, p) in ExistingProfiles)
        {
            var doc = await db.Doctors.Include(x => x.Account).FirstOrDefaultAsync(x => x.Account.Username == username);
            if (doc == null) continue;
            if (doc.AvatarUrl == null)
            {
                doc.AvatarUrl = p.Avatar;
                doc.AcademicTitle ??= p.Title;
                if (doc.YearsOfExperience == 0) doc.YearsOfExperience = p.Years;
                doc.IsPublic = true;
                doc.IsFeatured = true;
                doc.DisplayOrder = p.Order;
                doc.Biography = p.Bio;
                doc.ShortBio ??= p.Bio;
                await db.SaveChangesAsync();
            }
            var roomId = await db.DoctorSchedules.Where(s => s.DoctorId == doc.Id && s.RoomId != null)
                .Select(s => s.RoomId).FirstOrDefaultAsync();
            if (roomId == null)
            {
                var room = await db.ClinicRooms.FirstOrDefaultAsync(r => r.RoomNumber == "P103");
                if (room == null)
                {
                    room = new ClinicRoom { DepartmentId = doc.DepartmentId, RoomName = "Phòng khám Nội 3", RoomNumber = "P103" };
                    db.ClinicRooms.Add(room);
                    await db.SaveChangesAsync();
                }
                roomId = room.Id;
            }
            roomOf[doc.Id] = roomId.Value;
        }

        // 4. Kỳ tuần sau đã công bố + lịch đã duyệt T2–T7 để bệnh nhân đặt được ngay.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var offset = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var nextMonday = today.AddDays(offset == 0 ? 7 : offset);
        var nextSunday = nextMonday.AddDays(6);
        var period = await db.SchedulingPeriods.FirstOrDefaultAsync(p => p.StartDate == nextMonday && p.DepartmentId == null);
        if (period == null)
        {
            period = new SchedulingPeriod
            {
                Name = $"Kỳ làm việc tuần {nextMonday:dd/MM} - {nextSunday:dd/MM/yyyy}",
                StartDate = nextMonday,
                EndDate = nextSunday,
                BiddingOpensAt = DateTime.UtcNow.AddDays(-7),
                BiddingClosesAt = DateTime.UtcNow.AddDays(-1),
                Status = SchedulingPeriodStatus.Published,
                CreatedBy = reviewerAccountId,
                CreatedAt = DateTime.UtcNow
            };
            db.SchedulingPeriods.Add(period);
            await db.SaveChangesAsync();
        }

        var doctorIds = roomOf.Keys.ToList();
        var deptOfDoctor = await db.Doctors.Where(d => doctorIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.DepartmentId);
        var i = 0;
        foreach (var (doctorId, roomId) in roomOf)
        {
            i++;
            if (await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doctorId && s.WorkDate >= nextMonday && s.WorkDate <= nextSunday))
                continue;
            for (var day = 0; day < 6; day++)
            {
                var date = nextMonday.AddDays(day);
                db.DoctorSchedules.Add(Approved(doctorId, deptOfDoctor[doctorId], roomId, period.Id, date, ExamShift.Morning, reviewerAccountId));
                if ((day + i) % 2 == 0 && day < 5)
                    db.DoctorSchedules.Add(Approved(doctorId, deptOfDoctor[doctorId], roomId, period.Id, date, ExamShift.Afternoon, reviewerAccountId));
            }
            await db.SaveChangesAsync();
        }
    }

    private static DoctorSchedule Approved(int doctorId, int deptId, int roomId, int periodId, DateOnly date, ExamShift shift, int reviewer) => new()
    {
        DoctorId = doctorId,
        PeriodId = periodId,
        DepartmentId = deptId,
        RoomId = roomId,
        WorkDate = date,
        Shift = shift,
        StartTime = shift == ExamShift.Morning ? new TimeOnly(7, 30) : new TimeOnly(13, 0),
        EndTime = shift == ExamShift.Morning ? new TimeOnly(11, 30) : new TimeOnly(16, 30),
        SlotDurationMinutes = 30,
        MaxPatients = 30,
        OnlineSlots = 20,
        Status = DoctorScheduleStatus.Approved,
        ReviewedBy = reviewer,
        ReviewedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };
}
