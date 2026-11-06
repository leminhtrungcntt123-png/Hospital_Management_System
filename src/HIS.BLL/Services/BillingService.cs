using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Interfaces;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;

namespace HIS.BLL.Services;

public class BillingService : IBillingService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public BillingService(AppDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime Now => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    public async Task<IReadOnlyList<PendingInvoiceItemDto>> GetPendingInvoicesAsync(string? search = null, CancellationToken ct = default)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Invoice)
            .Where(a => a.StatusId >= (int)AppointmentStatusId.CheckedIn);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(a =>
                a.AppointmentCode.Contains(search)
                || a.Patient.FullName.Contains(search)
                || a.Patient.PhoneNumber.Contains(search)
                || (a.Patient.NationalId != null && a.Patient.NationalId.Contains(search))
                || (a.QueueNumber != null && a.QueueNumber.Contains(search)));
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var list = new List<PendingInvoiceItemDto>();

        foreach (var a in appointments)
        {
            var invoiceStatus = a.Invoice?.Status ?? InvoiceStatus.Unpaid;
            var hasInsurance = await _context.InsuranceCards
                .AnyAsync(c => c.PatientId == a.PatientId && c.IsActive && c.ValidFrom <= Today && c.ValidTo >= Today, ct);

            decimal totalAmount = a.Invoice?.TotalAmount ?? 150000m; // Phí khám mặc định

            var statusName = a.StatusId switch
            {
                (int)AppointmentStatusId.CheckedIn => "Đã tiếp nhận",
                (int)AppointmentStatusId.ReadyForDoctor => "Chờ khám",
                (int)AppointmentStatusId.InProgress => "Đang khám",
                (int)AppointmentStatusId.Completed => "Đã hoàn thành",
                _ => "Đang xử lý"
            };

            list.Add(new PendingInvoiceItemDto(
                a.Id,
                a.AppointmentCode,
                a.PatientId.ToString("D8"),
                a.Patient.FullName,
                a.Patient.PhoneNumber,
                a.Department?.Name ?? "Khoa khám",
                a.Doctor?.FullName ?? "Bác sĩ",
                a.Slot?.StartTime ?? a.CreatedAt,
                statusName,
                totalAmount,
                hasInsurance,
                invoiceStatus));
        }

        return list;
    }

    public async Task<Result<InvoiceDetailDto>> GetInvoiceDetailAsync(int appointmentId, CancellationToken ct = default)
    {
        var app = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.InvoiceItems)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        if (app == null)
        {
            return Result.Failure<InvoiceDetailDto>("NOT_FOUND", "Không tìm thấy thông tin lượt khám.");
        }

        var items = new List<InvoiceItemDto>();
        int itemId = 1;

        // 1. Phí khám bệnh ban đầu
        var examFee = await _context.Services
            .AsNoTracking()
            .Where(s => s.ServiceTypeId == 1 && s.IsActive)
            .Select(s => s.Price)
            .FirstOrDefaultAsync(ct);
        if (examFee <= 0) examFee = 150000m;

        items.Add(new InvoiceItemDto(
            itemId++,
            "Khám bệnh",
            $"Công khám chuyên khoa: {app.Department?.Name ?? "Nội tổng quát"}",
            examFee,
            1,
            examFee));

        // 2. Dịch vụ cận lâm sàng / Xét nghiệm / CĐHA từ hồ sơ bệnh án
        if (app.MedicalRecord != null)
        {
            foreach (var order in app.MedicalRecord.LabOrders)
            {
                var service = order.Service;
                if (service != null)
                {
                    var catName = service.ServiceType?.Name ?? "Xét nghiệm / CĐHA";
                    items.Add(new InvoiceItemDto(
                        itemId++,
                        catName,
                        service.Name,
                        service.Price,
                        1,
                        service.Price));
                }
            }

            // 3. Thuốc theo đơn
            foreach (var pres in app.MedicalRecord.Prescriptions)
            {
                foreach (var pi in pres.PrescriptionItems)
                {
                    if (pi.Medication != null)
                    {
                        var lineTotal = pi.Medication.Price * pi.Quantity;
                        items.Add(new InvoiceItemDto(
                            itemId++,
                            "Thuốc theo đơn",
                            $"{pi.Medication.Name} {pi.Medication.Strength ?? ""} ({pi.Dosage ?? ""})",
                            pi.Medication.Price,
                            pi.Quantity,
                            lineTotal));
                    }
                }
            }
        }

        decimal totalAmount = items.Sum(i => i.Total);

        // 4. Đối chiếu BHYT
        var insuranceCard = await _context.InsuranceCards
            .Include(c => c.InsurancePlan)
            .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= Today && c.ValidTo >= Today, ct);

        bool hasInsurance = insuranceCard != null;
        decimal coveragePct = hasInsurance ? (insuranceCard!.InsurancePlan?.CoveragePct ?? 80.0m) : 0.0m;

        decimal insuranceCoveredAmount = Math.Round(totalAmount * (coveragePct / 100.0m), 2);
        decimal patientAmount = totalAmount - insuranceCoveredAmount;

        var invoice = app.Invoice;
        var status = invoice?.Status ?? InvoiceStatus.Unpaid;
        var paidPayment = invoice?.Payments.OrderByDescending(p => p.PaidAt).FirstOrDefault();

        int age = Math.Max(1, Today.Year - app.Patient.DateOfBirth.Year);

        var detail = new InvoiceDetailDto(
            invoice?.Id ?? 0,
            app.Id,
            app.AppointmentCode,
            app.PatientId,
            app.PatientId.ToString("D8"),
            app.Patient.FullName,
            app.Patient.PhoneNumber,
            app.Patient.DateOfBirth,
            age,
            app.Patient.Gender == Gender.Male ? "Nam" : "Nữ",
            app.Doctor?.FullName ?? "Bác sĩ phụ trách",
            app.Department?.Name ?? "Khoa chuyên môn",
            app.Slot?.StartTime ?? app.CreatedAt,
            hasInsurance,
            insuranceCard?.CardNumber,
            coveragePct,
            insuranceCard?.RegisteredFacility ?? "Bệnh viện Đa khoa MediCare",
            items,
            totalAmount,
            insuranceCoveredAmount,
            patientAmount,
            status,
            paidPayment?.PaidAt,
            paidPayment?.PaymentMethod.ToString());

        return Result.Success(detail);
    }

    public async Task<Result<int>> CheckoutInvoiceAsync(CheckoutInvoiceRequest request, CancellationToken ct = default)
    {
        var app = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.InvoiceItems)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct);

        if (app == null)
        {
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy thông tin lượt khám.");
        }

        var detailRes = await GetInvoiceDetailAsync(request.AppointmentId, ct);
        if (!detailRes.IsSuccess)
        {
            return Result.Failure<int>(detailRes.Error.Code, detailRes.Error.Message);
        }

        var detail = detailRes.Value!;
        var now = Now;

        var insuranceCard = await _context.InsuranceCards
            .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= Today && c.ValidTo >= Today, ct);

        var invoice = app.Invoice;
        if (invoice != null && invoice.Status == InvoiceStatus.Paid)
        {
            return Result.Failure<int>("ALREADY_PAID", "Hóa đơn này đã được thanh toán trước đó.");
        }

        if (invoice == null)
        {
            invoice = new Invoice
            {
                AppointmentId = app.Id,
                PatientId = app.PatientId,
                CoveragePctApplied = detail.CoveragePct,
                TotalAmount = detail.TotalAmount,
                InsuranceCoveredAmount = detail.InsuranceCoveredAmount,
                PatientAmount = detail.PatientAmount,
                InsuranceCardId = insuranceCard?.Id,
                InsurancePlanId = insuranceCard?.InsurancePlanId,
                Status = InvoiceStatus.Paid,
                CreatedAt = now
            };

            foreach (var item in detail.Items)
            {
                invoice.InvoiceItems.Add(new InvoiceItem
                {
                    Category = item.Category.Contains("Thuốc") ? InvoiceCategory.Medication : (item.Category.Contains("Khám") ? InvoiceCategory.Examination : InvoiceCategory.Service),
                    Description = item.Description,
                    Amount = item.Total
                });
            }

            _context.Invoices.Add(invoice);
        }
        else
        {
            invoice.CoveragePctApplied = detail.CoveragePct;
            invoice.TotalAmount = detail.TotalAmount;
            invoice.InsuranceCoveredAmount = detail.InsuranceCoveredAmount;
            invoice.PatientAmount = detail.PatientAmount;
            invoice.InsuranceCardId = insuranceCard?.Id;
            invoice.InsurancePlanId = insuranceCard?.InsurancePlanId;
            invoice.Status = InvoiceStatus.Paid;
        }

        var payment = new Payment
        {
            Invoice = invoice,
            Amount = detail.PatientAmount,
            PaymentType = PaymentType.Payment,
            PaymentMethod = request.Method,
            CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
            Note = string.IsNullOrWhiteSpace(request.Note) ? "Thanh toán viện phí tại quầy thu ngân" : request.Note.Trim(),
            PaidAt = now
        };

        _context.Payments.Add(payment);

        if (request.CashierAccountId > 0)
        {
            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = request.CashierAccountId,
                PatientId = app.PatientId,
                Action = "CHECKOUT_INVOICE",
                OccurredAt = now
            });
        }

        await _context.SaveChangesAsync(ct);
        return Result.Success(invoice.Id);
    }
}

