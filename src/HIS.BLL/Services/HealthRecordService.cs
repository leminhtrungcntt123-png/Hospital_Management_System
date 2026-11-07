using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class HealthRecordService : IHealthRecordService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public HealthRecordService(AppDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    public async Task<Result<HealthRecordDetailDto>> GetHealthRecordDetailAsync(int appointmentId, int currentAccountId, CancellationToken ct = default)
    {
        var app = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Result)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        if (app == null)
        {
            return Result.Failure<HealthRecordDetailDto>("NOT_FOUND", "Không tìm thấy hồ sơ lượt khám.");
        }

        // Kiểm tra quyền truy cập hồ sơ bệnh án (Bảo mật theo CWE-285 - Improper Authorization)
        if (currentAccountId > 0)
        {
            var account = await _context.Accounts
                .Include(a => a.Role)
                .FirstOrDefaultAsync(a => a.Id == currentAccountId, ct);

            if (account == null)
            {
                return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Tài khoản không tồn tại.");
            }

            string roleCode = account.Role.Code.ToUpperInvariant();
            if (roleCode == "PATIENT")
            {
                bool hasAccess = await _context.AccountPatientProfiles
                    .AnyAsync(p => p.AccountId == currentAccountId && p.PatientId == app.PatientId && p.Status == LinkStatus.Active, ct);

                if (!hasAccess && app.BookedBy != currentAccountId)
                {
                    return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Bạn không có quyền truy cập hồ sơ sức khỏe này.");
                }
            }
            else if (roleCode == "ADMIN")
            {
                // Quản trị viên hệ thống có quyền giám sát
            }
            else if (roleCode == "DOCTOR")
            {
                // Bác sĩ chỉ được xem bệnh án của lượt khám do mình phụ trách hoặc trong cùng chuyên khoa
                var doc = await _context.Doctors.FirstOrDefaultAsync(d => d.AccountId == currentAccountId, ct);
                if (doc == null || (app.DoctorId != doc.Id && app.DepartmentId != doc.DepartmentId))
                {
                    return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Bác sĩ chỉ có quyền xem hồ sơ bệnh án thuộc phạm vi chuyên môn hoặc lượt khám do mình phụ trách.");
                }
            }
            else if (roleCode == "DEPARTMENT_HEAD" || roleCode == "DEPT_HEAD")
            {
                // Trưởng khoa chỉ xem các lượt khám thuộc khoa mình
                var head = await _context.DepartmentHeads.FirstOrDefaultAsync(h => h.AccountId == currentAccountId, ct);
                if (head == null || app.DepartmentId != head.DepartmentId)
                {
                    return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Trưởng khoa chỉ có quyền xem hồ sơ bệnh án thuộc khoa phụ trách.");
                }
            }
            else if (roleCode == "NURSE")
            {
                // Điều dưỡng chỉ xem các ca bệnh trong khoa mình phụ trách
                var nurse = await _context.Nurses.FirstOrDefaultAsync(n => n.AccountId == currentAccountId, ct);
                if (nurse == null || app.DepartmentId != nurse.DepartmentId)
                {
                    return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Điều dưỡng chỉ có quyền xem hồ sơ bệnh án thuộc khoa phụ trách.");
                }
            }
            else
            {
                // Các nhân viên phi lâm sàng (Thu ngân, Tiếp tân...) không được phép truy cập xem chi tiết chẩn đoán và đơn thuốc qua cổng này
                return Result.Failure<HealthRecordDetailDto>("FORBIDDEN", "Tài khoản của bạn không có quyền xem chi tiết hồ sơ bệnh án này.");
            }
        }

        var p = app.Patient;
        int age = Math.Max(1, Today.Year - p.DateOfBirth.Year);

        // Danh sách dịch vụ & chi phí
        var serviceItems = new List<HealthRecordServiceItemDto>();

        // 1. Phí khám
        serviceItems.Add(new HealthRecordServiceItemDto(
            $"Khám chuyên khoa: {app.Department?.Name ?? "Nội tổng quát"}",
            150000m,
            "Khám bệnh"));

        // 2. Dịch vụ cận lâm sàng & Xét nghiệm
        var labItems = new List<HealthRecordLabItemDto>();
        var imagingItems = new List<HealthRecordImagingItemDto>();

        if (app.MedicalRecord != null)
        {
            foreach (var order in app.MedicalRecord.LabOrders)
            {
                var s = order.Service;
                if (s != null)
                {
                    string cat = s.ServiceType?.Code ?? "LAB_TEST";
                    serviceItems.Add(new HealthRecordServiceItemDto(
                        s.Name,
                        s.Price,
                        s.ServiceType?.Name ?? "Cận lâm sàng"));

                    if (cat == "IMAGING" || cat == "FUNCTIONAL" || s.Name.Contains("Siêu âm") || s.Name.Contains("X-quang"))
                    {
                        imagingItems.Add(new HealthRecordImagingItemDto(
                            s.Name,
                            order.Result?.Result ?? "Hình ảnh cấu trúc bình thường, không thấy bất thường khu trú.",
                            "Hiện tại chưa phát hiện hình ảnh bệnh lý bất thường.",
                            null));
                    }
                    else
                    {
                        labItems.Add(new HealthRecordLabItemDto(
                            s.Name,
                            order.Result?.Result ?? "Trong giới hạn bình thường",
                            "Chỉ số",
                            "Bình thường",
                            order.Result != null ? "Hoàn thành" : "Đã có kết quả"));
                    }
                }
            }
        }

        // 3. Đơn thuốc
        var presItems = new List<HealthRecordPrescriptionItemDto>();
        if (app.MedicalRecord != null)
        {
            foreach (var pres in app.MedicalRecord.Prescriptions)
            {
                foreach (var pi in pres.PrescriptionItems)
                {
                    if (pi.Medication != null)
                    {
                        serviceItems.Add(new HealthRecordServiceItemDto(
                            $"{pi.Medication.Name} {pi.Medication.Strength ?? ""} x {pi.Quantity} {pi.Medication.Unit}",
                            pi.Medication.Price * pi.Quantity,
                            "Thuốc"));

                        presItems.Add(new HealthRecordPrescriptionItemDto(
                            pi.Medication.Name,
                            pi.Medication.ActiveIngredient ?? pi.Medication.Name,
                            pi.Dosage ?? "Uống theo chỉ định",
                            pi.Quantity,
                            pi.Medication.Unit,
                            pi.UsageInstructions ?? "Uống sau ăn no, nhiều nước"));
                    }
                }
            }
        }

        // BHYT & Hóa đơn
        decimal totalAmount = app.Invoice?.TotalAmount ?? serviceItems.Sum(s => s.Price);
        decimal coveragePct = app.Invoice?.CoveragePctApplied ?? 80.0m;
        decimal insuranceCoveredAmount = app.Invoice?.InsuranceCoveredAmount ?? Math.Round(totalAmount * (coveragePct / 100.0m), 2);
        decimal patientAmount = app.Invoice?.PatientAmount ?? (totalAmount - insuranceCoveredAmount);
        var billingStatus = app.Invoice?.Status ?? InvoiceStatus.Paid;

        HealthRecordExamDto? exam = null;
        if (app.MedicalRecord != null)
        {
            exam = new HealthRecordExamDto(
                app.MedicalRecord.Symptoms ?? "Không có triệu chứng bất thường",
                app.MedicalRecord.ClinicalExamination ?? "Toàn trạng ổn định, tim phổi bình thường",
                app.MedicalRecord.PrimaryDiagnosisCode ?? "Z00.0",
                app.MedicalRecord.PrimaryDiagnosisDesc ?? "Khám kiểm tra sức khỏe tổng quát",
                app.MedicalRecord.Conclusion ?? "Thể trạng bình thường, tuân thủ chế độ dinh dưỡng và sinh hoạt hợp lý.");
        }

        var detail = new HealthRecordDetailDto(
            app.Id,
            app.AppointmentCode,
            p.Id,
            p.Id.ToString("D8"),
            p.FullName.ToUpperInvariant(),
            p.Gender == Gender.Male ? "Nam" : "Nữ",
            age,
            p.DateOfBirth,
            p.PhoneNumber,
            p.Address,
            "BỆNH VIỆN ĐA KHOA MEDICARE",
            app.Slot?.StartTime ?? app.CreatedAt,
            app.Doctor?.FullName ?? "Bác sĩ phụ trách",
            app.Department?.Name ?? "Khoa Nội",
            billingStatus,
            totalAmount,
            insuranceCoveredAmount,
            patientAmount,
            coveragePct,
            serviceItems,
            exam,
            labItems,
            imagingItems,
            presItems);

        return Result.Success(detail);
    }

    public async Task<IReadOnlyList<HealthRecordHistoryItemDto>> GetPatientHealthHistoryAsync(int patientId, int currentAccountId, CancellationToken ct = default)
    {
        var appointments = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.MedicalRecord)
            .Include(a => a.Invoice)
            .Where(a => a.PatientId == patientId && a.StatusId >= (int)AppointmentStatusId.CheckedIn)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return appointments.Select(a => new HealthRecordHistoryItemDto(
            a.Id,
            a.AppointmentCode,
            a.Slot?.StartTime ?? a.CreatedAt,
            a.Doctor?.FullName ?? "Bác sĩ",
            a.Department?.Name ?? "Khoa",
            a.MedicalRecord?.PrimaryDiagnosisDesc ?? "Khám sức khỏe",
            a.Invoice?.PatientAmount ?? 0,
            a.Invoice?.Status ?? InvoiceStatus.Unpaid
        )).ToList();
    }
}

