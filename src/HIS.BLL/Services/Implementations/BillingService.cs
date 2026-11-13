using System.Data;
using HIS.BLL.Common;
using HIS.BLL.DTOs;
using HIS.BLL.Options;
using HIS.DAL;
using HIS.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HIS.BLL.Services;

public class BillingService : IBillingService
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly BillingOptions _options;

    public BillingService(
        AppDbContext context,
        TimeProvider? timeProvider = null,
        IOptions<BillingOptions>? options = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? new BillingOptions();
    }

    private DateTime Now => DateTime.SpecifyKind(_timeProvider.GetLocalNow().DateTime, DateTimeKind.Unspecified);
    private DateOnly Today => DateOnly.FromDateTime(Now);

    private async Task<Result<T>> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction != null)
        {
            return await action(ct);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            var result = await action(ct);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }

            return result;
        });
    }

    private Task<CashierShift?> LockOpenShiftAsync(int cashierAccountId, CancellationToken ct)
    {
        if (cashierAccountId <= 0)
        {
            return Task.FromResult<CashierShift?>(null);
        }

        return _context.CashierShifts
            .FromSqlInterpolated($"SELECT * FROM cashier_shifts WHERE cashier_id = {cashierAccountId} AND status = 'OPEN' FOR UPDATE")
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PendingInvoiceItemDto>> GetPendingInvoicesAsync(
        string? search = null,
        InvoiceStatus? invoiceStatus = null,
        CancellationToken ct = default)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .Where(a => a.StatusId >= (int)AppointmentStatusId.CheckedIn);

        if (invoiceStatus == InvoiceStatus.Paid)
        {
            query = query.Where(a => a.Invoice != null && a.Invoice.Status == InvoiceStatus.Paid);
        }
        else
        {
            query = query.Where(a => a.Invoice == null || a.Invoice.Status == InvoiceStatus.Unpaid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            string searchUpper = search.ToUpperInvariant();
            bool hasIntId = TryParsePatientId(search, out var parsedId);

            query = query.Where(a =>
                (hasIntId && a.PatientId == parsedId)
                || a.AppointmentCode.ToUpper() == searchUpper
                || (a.Patient.NationalId != null && a.Patient.NationalId == search));
        }
        else
        {
            query = query.Where(a => !a.Patient.FullName.StartsWith("BN Test") && !a.Patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"));
        }

        var appointments = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var patientIds = appointments.Select(a => a.PatientId).Distinct().ToList();

        var insuranceCards = await _context.InsuranceCards
            .AsNoTracking()
            .Include(c => c.InsurancePlan)
            .Where(c => patientIds.Contains(c.PatientId) && c.IsActive)
            .ToListAsync(ct);

        var patientDeposits = await _context.Deposits
            .AsNoTracking()
            .Where(d => patientIds.Contains(d.PatientId) && d.Status == DepositStatus.Active && (d.Amount - d.UsedAmount - d.RefundedAmount) > 0)
            .GroupBy(d => d.PatientId)
            .Select(g => new
            {
                PatientId = g.Key,
                Balance = g.Sum(d => d.Amount - d.UsedAmount - d.RefundedAmount)
            })
            .ToDictionaryAsync(x => x.PatientId, x => x.Balance, ct);

        var list = new List<PendingInvoiceItemDto>();

        foreach (var a in appointments)
        {
            var examDate = a.Slot != null ? DateOnly.FromDateTime(a.Slot.StartTime) : DateOnly.FromDateTime(a.CreatedAt);
            var itemInvoiceStatus = a.Invoice?.Status ?? InvoiceStatus.Unpaid;

            var card = insuranceCards.FirstOrDefault(c => c.PatientId == a.PatientId && c.ValidFrom <= examDate && c.ValidTo >= examDate);
            bool hasInsurance = card != null;
            decimal baseCoveragePct = 0m;
            if (hasInsurance && card != null)
            {
                bool isRightRoute = card.IsRegisteredHere || card.InsurancePlan?.ReferralType == ReferralType.InNetwork;
                baseCoveragePct = isRightRoute ? (card.InsurancePlan?.CoveragePct ?? 80m) : _options.OutOfRoutePct;
            }

            decimal totalAmount;
            decimal patientPayAmount;

            if (a.Invoice != null)
            {
                totalAmount = a.Invoice.TotalAmount;
                patientPayAmount = a.Invoice.PatientAmount;
            }
            else
            {
                decimal examPrice = _options.DefaultExamPrice;
                decimal examCovered = hasInsurance ? Math.Round(examPrice * baseCoveragePct / 100m, 0, MidpointRounding.AwayFromZero) : 0m;
                decimal examPatient = examPrice - examCovered;

                decimal labsTotal = 0m;
                decimal labsCovered = 0m;
                if (a.MedicalRecord?.LabOrders != null)
                {
                    foreach (var order in a.MedicalRecord.LabOrders)
                    {
                        if (order.Service != null)
                        {
                            labsTotal += order.Service.Price;
                            if (hasInsurance && order.Service.IsInsuranceCovered)
                            {
                                labsCovered += Math.Round(order.Service.Price * baseCoveragePct / 100m, 0, MidpointRounding.AwayFromZero);
                            }
                        }
                    }
                }

                decimal medsTotal = 0m;
                decimal medsCovered = 0m;
                if (a.MedicalRecord?.Prescriptions != null)
                {
                    foreach (var pres in a.MedicalRecord.Prescriptions)
                    {
                        foreach (var pi in pres.PrescriptionItems)
                        {
                            if (pi.Medication != null)
                            {
                                decimal lineTotal = pi.Medication.Price * pi.Quantity;
                                medsTotal += lineTotal;
                                if (hasInsurance && pi.Medication.IsInsuranceCovered)
                                {
                                    medsCovered += Math.Round(lineTotal * baseCoveragePct / 100m, 0, MidpointRounding.AwayFromZero);
                                }
                            }
                        }
                    }
                }

                totalAmount = examPrice + labsTotal + medsTotal;
                patientPayAmount = examPatient + (labsTotal - labsCovered) + (medsTotal - medsCovered);
            }

            decimal depositBalance = patientDeposits.TryGetValue(a.PatientId, out var depBal) ? depBal : 0m;

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
                examDate,
                statusName,
                totalAmount,
                hasInsurance,
                itemInvoiceStatus,
                a.Invoice?.InvoiceNumber,
                a.Patient.NationalId,
                depositBalance,
                patientPayAmount));
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
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)!.ThenInclude(p => p.Cashier)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        if (app == null)
        {
            return Result.Failure<InvoiceDetailDto>("NOT_FOUND", "Không tìm thấy thông tin lượt khám.");
        }

        var examDate = app.Slot != null ? DateOnly.FromDateTime(app.Slot.StartTime) : DateOnly.FromDateTime(app.CreatedAt);

        // Kiểm tra thẻ BHYT hợp lệ THEO NGÀY KHÁM (Quy tắc bất biến HIS DECISIONS #5)
        var insuranceCard = await _context.InsuranceCards
            .AsNoTracking()
            .Include(c => c.InsurancePlan)
            .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= examDate && c.ValidTo >= examDate, ct);

        bool hasInsurance = insuranceCard != null;
        decimal baseCoveragePct = 0m;
        string? cardNum = null;
        string? facility = null;

        if (hasInsurance && insuranceCard != null)
        {
            cardNum = insuranceCard.CardNumber;
            facility = insuranceCard.RegisteredFacility;

            bool isRightRoute = insuranceCard.IsRegisteredHere || insuranceCard.InsurancePlan?.ReferralType == ReferralType.InNetwork;
            baseCoveragePct = isRightRoute ? (insuranceCard.InsurancePlan?.CoveragePct ?? 80m) : _options.OutOfRoutePct;
        }

        var items = new List<InvoiceItemDto>();
        int itemId = 1;

        // 1. Phí công khám
        var examService = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _options.ExamFeeServiceId, ct);
        decimal examUnitPrice = examService?.Price ?? _options.DefaultExamPrice;
        bool examCovered = examService?.IsInsuranceCovered ?? true;
        decimal examLineCoverage = (hasInsurance && examCovered) ? baseCoveragePct : 0m;
        decimal examCoveredAmount = Math.Round(examUnitPrice * examLineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
        decimal examPatientAmount = examUnitPrice - examCoveredAmount;

        items.Add(new InvoiceItemDto(
            itemId++,
            "Công khám",
            examService?.Name ?? "Khám chuyên khoa",
            examUnitPrice,
            1,
            examUnitPrice,
            examCovered,
            examLineCoverage,
            examCoveredAmount,
            examPatientAmount));

        // 2. Phí cận lâm sàng / Xét nghiệm
        if (app.MedicalRecord?.LabOrders != null)
        {
            foreach (var order in app.MedicalRecord.LabOrders)
            {
                if (order.Service != null)
                {
                    decimal unitPrice = order.Service.Price;
                    bool isCovered = order.Service.IsInsuranceCovered;
                    decimal lineCoverage = (hasInsurance && isCovered) ? baseCoveragePct : 0m;
                    decimal coveredAmount = Math.Round(unitPrice * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                    decimal patientAmount = unitPrice - coveredAmount;

                    items.Add(new InvoiceItemDto(
                        itemId++,
                        order.Service.ServiceType?.Name ?? "Cận lâm sàng",
                        order.Service.Name,
                        unitPrice,
                        1,
                        unitPrice,
                        isCovered,
                        lineCoverage,
                        coveredAmount,
                        patientAmount));
                }
            }
        }

        // 3. Phí thuốc theo đơn
        if (app.MedicalRecord?.Prescriptions != null)
        {
            foreach (var p in app.MedicalRecord.Prescriptions)
            {
                foreach (var pi in p.PrescriptionItems)
                {
                    if (pi.Medication != null)
                    {
                        decimal unitPrice = pi.Medication.Price;
                        int qty = pi.Quantity;
                        decimal total = unitPrice * qty;
                        bool isCovered = pi.Medication.IsInsuranceCovered;
                        decimal lineCoverage = (hasInsurance && isCovered) ? baseCoveragePct : 0m;
                        decimal coveredAmount = Math.Round(total * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                        decimal patientAmount = total - coveredAmount;

                        items.Add(new InvoiceItemDto(
                            itemId++,
                            "Thuốc kê đơn",
                            $"{pi.Medication.Name} ({pi.Medication.Unit})",
                            unitPrice,
                            qty,
                            total,
                            isCovered,
                            lineCoverage,
                            coveredAmount,
                            patientAmount));
                    }
                }
            }
        }

        decimal totalAmount = items.Sum(i => i.Total);
        decimal insuranceCoveredAmount = items.Sum(i => i.CoveredAmount);
        decimal patientAmountTotal = items.Sum(i => i.PatientAmount);

        var invoice = app.Invoice;
        var allPayments = invoice?.Payments.OrderBy(p => p.PaidAt).ToList() ?? new List<Payment>();
        var payment = allPayments.FirstOrDefault(p => p.PaymentType == PaymentType.Payment && p.PaymentMethod != PaymentMethod.DepositDeduction)
                      ?? allPayments.FirstOrDefault(p => p.PaymentType == PaymentType.Payment)
                      ?? allPayments.LastOrDefault();

        decimal depositDeducted = allPayments
            .Where(p => p.PaymentType == PaymentType.Payment && p.PaymentMethod == PaymentMethod.DepositDeduction)
            .Sum(p => p.Amount);
        decimal additionalPaid = allPayments
            .Where(p => p.PaymentType == PaymentType.Payment && p.PaymentMethod != PaymentMethod.DepositDeduction)
            .Sum(p => p.Amount);
        decimal refundedAmount = allPayments
            .Where(p => p.PaymentType == PaymentType.Refund)
            .Sum(p => p.Amount);

        string? paymentMethodDisplay = null;
        if (allPayments.Count > 0)
        {
            if (depositDeducted > 0)
            {
                if (additionalPaid > 0)
                {
                    var extraMethod = payment?.PaymentMethod == PaymentMethod.BankTransfer ? "Chuyển khoản" : "Tiền mặt";
                    paymentMethodDisplay = $"Cấn trừ tạm ứng + Thu thêm ({extraMethod})";
                }
                else if (refundedAmount > 0)
                {
                    paymentMethodDisplay = "Cấn trừ tạm ứng & Hoàn trả tiền thừa";
                }
                else
                {
                    paymentMethodDisplay = "Cấn trừ tạm ứng 100%";
                }
            }
            else if (payment != null)
            {
                paymentMethodDisplay = payment.PaymentMethod == PaymentMethod.Cash
                    ? "Tiền mặt"
                    : (payment.PaymentMethod == PaymentMethod.BankTransfer ? "Chuyển khoản" : "Cấn trừ tạm ứng");
            }
        }

        string? transactionCodesDisplay = allPayments.Count > 0
            ? string.Join(", ", allPayments.Select(p => p.TransactionCode).Distinct())
            : null;

        int age = examDate.Year - app.Patient.DateOfBirth.Year;
        string genderStr = app.Patient.Gender switch
        {
            Gender.Male => "Nam",
            Gender.Female => "Nữ",
            _ => "Khác"
        };

        // Lấy các khoản tạm ứng khả dụng của bệnh nhân
        var activeDeposits = await _context.Deposits
            .AsNoTracking()
            .Include(d => d.Patient)
            .Include(d => d.Cashier)
            .Include(d => d.Appointment)
            .Where(d => d.PatientId == app.PatientId && d.Status == DepositStatus.Active && (d.Amount - d.UsedAmount - d.RefundedAmount) > 0)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        decimal availableDepositBalance = activeDeposits.Sum(d => d.Amount - d.UsedAmount - d.RefundedAmount);

        var displayDeposits = activeDeposits;
        if (displayDeposits.Count == 0 && invoice?.Status == InvoiceStatus.Paid)
        {
            displayDeposits = await _context.Deposits
                .AsNoTracking()
                .Include(d => d.Patient)
                .Include(d => d.Cashier)
                .Include(d => d.Appointment)
                .Where(d => d.PatientId == app.PatientId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct);
        }

        var depositDtos = displayDeposits.Select(d => new DepositDto(
            d.Id,
            d.DepositCode,
            d.PatientId,
            d.PatientId.ToString("D8"),
            app.Patient.FullName,
            app.Patient.PhoneNumber,
            d.AppointmentId,
            d.Appointment?.AppointmentCode,
            d.Amount,
            d.UsedAmount,
            d.RefundedAmount,
            d.Amount - d.UsedAmount - d.RefundedAmount,
            d.Status,
            d.PaymentMethod,
            d.ReferenceCode,
            d.Purpose,
            d.Note,
            d.Cashier?.Username ?? "Hệ thống",
            d.CreatedAt,
            d.Patient.NationalId,
            d.ApprovedBy)).ToList();

        string? diagnosisText = app.MedicalRecord != null && !string.IsNullOrWhiteSpace(app.MedicalRecord.PrimaryDiagnosisDesc)
            ? (!string.IsNullOrWhiteSpace(app.MedicalRecord.PrimaryDiagnosisCode)
                ? $"[{app.MedicalRecord.PrimaryDiagnosisCode}] {app.MedicalRecord.PrimaryDiagnosisDesc}"
                : app.MedicalRecord.PrimaryDiagnosisDesc)
            : "Khám và theo dõi chuyên khoa";

        var detail = new InvoiceDetailDto(
            invoice?.Id ?? 0,
            invoice?.InvoiceNumber ?? "Chưa lập",
            app.Id,
            app.AppointmentCode,
            examDate,
            app.PatientId,
            app.PatientId.ToString("D8"),
            app.Patient.FullName,
            app.Patient.PhoneNumber,
            app.Patient.DateOfBirth,
            age,
            genderStr,
            app.Doctor?.FullName ?? "Bác sĩ",
            app.Department?.Name ?? "Khoa",
            app.Slot?.StartTime ?? app.CreatedAt,
            hasInsurance,
            cardNum,
            baseCoveragePct,
            facility,
            items,
            totalAmount,
            insuranceCoveredAmount,
            patientAmountTotal,
            invoice?.Status ?? InvoiceStatus.Unpaid,
            payment?.PaidAt,
            paymentMethodDisplay,
            transactionCodesDisplay,
            payment?.Cashier?.Username,
            availableDepositBalance,
            depositDtos,
            depositDeducted,
            additionalPaid,
            refundedAmount,
            app.Patient.NationalId,
            app.Patient.Address,
            diagnosisText);

        return Result.Success(detail);
    }

    public async Task<Result<int>> CheckoutInvoiceAsync(CheckoutInvoiceRequest request, CancellationToken ct = default)
    {
        if (request.AppointmentId <= 0)
        {
            return Result.Failure<int>("INVALID_REQUEST", "Mã lượt khám không hợp lệ.");
        }

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            // 1. Kiểm tra IdempotencyKey nếu có để chống ghi trùng
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingPayment = await _context.Payments
                    .AsNoTracking()
                    .Include(p => p.Invoice)
                    .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, innerCt);

                if (existingPayment != null)
                {
                    return Result.Success(existingPayment.InvoiceId ?? 0);
                }
            }

            var app = await _context.Appointments
                .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {request.AppointmentId} FOR UPDATE")
                .Include(a => a.Patient)
                .Include(a => a.Slot)
                .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
                .FirstOrDefaultAsync(innerCt);

            if (app == null)
            {
                return Result.Failure<int>("NOT_FOUND", "Không tìm thấy thông tin lượt khám.");
            }

            if (app.Invoice != null && (app.Invoice.Status == InvoiceStatus.Paid || app.Invoice.Payments.Any(p => p.PaymentType == PaymentType.Payment)))
            {
                return Result.Failure<int>("ALREADY_PAID", "Lượt khám này đã được thanh toán viện phí trước đó.");
            }

            var detailRes = await GetInvoiceDetailAsync(request.AppointmentId, innerCt);
            if (!detailRes.IsSuccess)
            {
                return Result.Failure<int>(detailRes.Error.Code, detailRes.Error.Message);
            }

            var detail = detailRes.Value!;
            var now = Now;
            var examDate = detail.ExamDate;

            var insuranceCard = await _context.InsuranceCards
                .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= examDate && c.ValidTo >= examDate, innerCt);

            // Bắt buộc thu ngân phải có ca đang mở (khóa hàng ca thu ngân)
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<int>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện thu tiền.");
            }

            // Sinh số hóa đơn và mã giao dịch từ sequence DB
            int invoiceSeq = await GetNextSequenceValueAsync("seq_invoice_number", innerCt);
            int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);

            string invoiceNum = $"HD-{invoiceSeq:D6}";
            string transCode = $"PT-{transSeq:D8}";

            var invoice = app.Invoice;
            if (invoice == null)
            {
                invoice = new Invoice
                {
                    AppointmentId = app.Id,
                    PatientId = app.PatientId,
                    InvoiceNumber = invoiceNum,
                    ExamDate = examDate,
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
                    InvoiceCategory cat = item.Category.Contains("Thuốc") ? InvoiceCategory.Medication
                        : (item.Category.Contains("Khám") ? InvoiceCategory.Examination : InvoiceCategory.Service);

                    invoice.InvoiceItems.Add(new InvoiceItem
                    {
                        Category = cat,
                        Description = item.Description,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity,
                        Amount = item.Total,
                        IsInsuranceCovered = item.IsInsuranceCovered,
                        CoveragePctApplied = item.CoveragePctApplied,
                        CoveredAmount = item.CoveredAmount,
                        PatientAmount = item.PatientAmount
                    });
                }

                _context.Invoices.Add(invoice);
            }
            else
            {
                invoice.InvoiceNumber = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoiceNum : invoice.InvoiceNumber;
                invoice.ExamDate = examDate;
                invoice.CoveragePctApplied = detail.CoveragePct;
                invoice.TotalAmount = detail.TotalAmount;
                invoice.InsuranceCoveredAmount = detail.InsuranceCoveredAmount;
                invoice.PatientAmount = detail.PatientAmount;
                invoice.InsuranceCardId = insuranceCard?.Id;
                invoice.InsurancePlanId = insuranceCard?.InsurancePlanId;
                invoice.Status = InvoiceStatus.Paid;
            }

            // Kiểm tra và cấn trừ tạm ứng nếu được yêu cầu (khóa hàng tạm ứng)
            decimal depositDeduct = 0m;
            if (request.DepositId.HasValue && request.DepositDeductAmount.HasValue && request.DepositDeductAmount.Value > 0)
            {
                var depositEntity = await _context.Deposits
                    .FromSqlInterpolated($"SELECT * FROM deposits WHERE id = {request.DepositId.Value} AND status = 'ACTIVE' FOR UPDATE")
                    .FirstOrDefaultAsync(innerCt);

                if (depositEntity == null)
                {
                    return Result.Failure<int>("DEPOSIT_NOT_FOUND", "Không tìm thấy thông tin phiếu tạm ứng hợp lệ.");
                }

                if (depositEntity.PatientId != app.PatientId)
                {
                    return Result.Failure<int>("DEPOSIT_MISMATCH", "Phiếu tạm ứng không thuộc về bệnh nhân này.");
                }

                decimal maxAvailable = depositEntity.Amount - depositEntity.UsedAmount - depositEntity.RefundedAmount;
                if (request.DepositDeductAmount.Value > maxAvailable)
                {
                    return Result.Failure<int>("INSUFFICIENT_DEPOSIT", $"Số tiền trừ ({request.DepositDeductAmount.Value:N0} đ) vượt quá số dư tạm ứng còn lại ({maxAvailable:N0} đ).");
                }

                depositDeduct = Math.Min(detail.PatientAmount, request.DepositDeductAmount.Value);
                depositEntity.UsedAmount += depositDeduct;
                if (depositEntity.Amount - depositEntity.UsedAmount - depositEntity.RefundedAmount <= 0)
                {
                    depositEntity.Status = DepositStatus.Depleted;
                }

                _context.DepositUsages.Add(new DepositUsage
                {
                    Deposit = depositEntity,
                    Invoice = invoice,
                    Amount = depositDeduct,
                    UsedAt = now,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null
                });

                int transSeqDeposit = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                _context.Payments.Add(new Payment
                {
                    Invoice = invoice,
                    Deposit = depositEntity,
                    TransactionCode = $"PT-{transSeqDeposit:D8}",
                    Amount = depositDeduct,
                    PaymentType = PaymentType.Payment,
                    PaymentMethod = PaymentMethod.DepositDeduction,
                    Status = PaymentStatus.Confirmed,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ShiftId = shift?.Id,
                    Note = $"Trừ từ phiếu tạm ứng {depositEntity.DepositCode}",
                    PaidAt = now
                });
            }

            // Phần còn lại bệnh nhân thanh toán bằng Tiền mặt / Chuyển khoản (nếu có)
            decimal remainingToPay = detail.PatientAmount - depositDeduct;
            if (remainingToPay > 0)
            {
                transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                transCode = $"PT-{transSeq:D8}";

                var payment = new Payment
                {
                    Invoice = invoice,
                    TransactionCode = transCode,
                    ReferenceCode = request.ReferenceCode?.Trim(),
                    Amount = remainingToPay,
                    PaymentType = PaymentType.Payment,
                    PaymentMethod = request.Method,
                    Status = PaymentStatus.Confirmed,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ShiftId = shift?.Id,
                    IdempotencyKey = request.IdempotencyKey,
                    Note = string.IsNullOrWhiteSpace(request.Note) ? "Thu viện phí tại quầy thu ngân" : request.Note.Trim(),
                    PaidAt = now
                };

                _context.Payments.Add(payment);
            }

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

            await _context.SaveChangesAsync(innerCt);
            return Result.Success(invoice.Id);
        }, ct);
    }

    public async Task<Result<SettleDischargeResultDto>> SettleDischargeAsync(SettleDischargeRequest request, CancellationToken ct = default)
    {
        if (request.AppointmentId <= 0)
        {
            return Result.Failure<SettleDischargeResultDto>("INVALID_REQUEST", "Mã lượt khám không hợp lệ.");
        }

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var app = await _context.Appointments
                .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {request.AppointmentId} FOR UPDATE")
                .Include(a => a.Patient)
                .Include(a => a.Slot)
                .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
                .FirstOrDefaultAsync(innerCt);

            if (app == null)
            {
                return Result.Failure<SettleDischargeResultDto>("NOT_FOUND", "Không tìm thấy thông tin lượt khám.");
            }

            if (app.Invoice != null && app.Invoice.Status == InvoiceStatus.Paid)
            {
                return Result.Failure<SettleDischargeResultDto>("ALREADY_PAID", "Lượt khám này đã hoàn tất quyết toán trước đó.");
            }

            var detailRes = await GetInvoiceDetailAsync(request.AppointmentId, innerCt);
            if (!detailRes.IsSuccess)
            {
                return Result.Failure<SettleDischargeResultDto>(detailRes.Error.Code, detailRes.Error.Message);
            }

            var detail = detailRes.Value!;
            var now = Now;
            var examDate = detail.ExamDate;

            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<SettleDischargeResultDto>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện quyết toán.");
            }

            // Lấy và khóa tất cả các phiếu tạm ứng khả dụng của bệnh nhân
            var activeDeposits = await _context.Deposits
                .FromSqlInterpolated($"SELECT * FROM deposits WHERE patient_id = {app.PatientId} AND status = 'ACTIVE' AND (amount - used_amount - refunded_amount) > 0 ORDER BY created_at FOR UPDATE")
                .ToListAsync(innerCt);

            // Sinh số hóa đơn và mã giao dịch từ sequence DB
            int invoiceSeq = await GetNextSequenceValueAsync("seq_invoice_number", innerCt);
            string invoiceNum = $"HD-{invoiceSeq:D6}";

            var invoice = app.Invoice;
            if (invoice == null)
            {
                invoice = new Invoice
                {
                    AppointmentId = app.Id,
                    PatientId = app.PatientId,
                    InvoiceNumber = invoiceNum,
                    ExamDate = examDate,
                    CoveragePctApplied = detail.CoveragePct,
                    TotalAmount = detail.TotalAmount,
                    InsuranceCoveredAmount = detail.InsuranceCoveredAmount,
                    PatientAmount = detail.PatientAmount,
                    Status = InvoiceStatus.Paid,
                    CreatedAt = now
                };

                foreach (var item in detail.Items)
                {
                    InvoiceCategory cat = item.Category.Contains("Thuốc") ? InvoiceCategory.Medication
                        : (item.Category.Contains("Khám") ? InvoiceCategory.Examination : InvoiceCategory.Service);

                    invoice.InvoiceItems.Add(new InvoiceItem
                    {
                        Category = cat,
                        Description = item.Description,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity,
                        Amount = item.Total,
                        IsInsuranceCovered = item.IsInsuranceCovered,
                        CoveragePctApplied = item.CoveragePctApplied,
                        CoveredAmount = item.CoveredAmount,
                        PatientAmount = item.PatientAmount
                    });
                }

                _context.Invoices.Add(invoice);
            }
            else
            {
                invoice.Status = InvoiceStatus.Paid;
                if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber) || invoice.InvoiceNumber == "Chưa lập")
                {
                    invoice.InvoiceNumber = invoiceNum;
                }
            }

            decimal patientRemainingOwed = detail.PatientAmount;
            decimal depositDeductedTotal = 0m;
            decimal additionalPaid = 0m;
            decimal refundedToPatient = 0m;
            string mainTransCode = string.Empty;

            // 1. Tự động cấn trừ từ các phiếu tạm ứng khả dụng
            foreach (var dep in activeDeposits)
            {
                decimal depBalance = dep.Amount - dep.UsedAmount - dep.RefundedAmount;
                if (depBalance <= 0) continue;

                if (patientRemainingOwed > 0)
                {
                    decimal deduct = Math.Min(patientRemainingOwed, depBalance);
                    dep.UsedAmount += deduct;
                    patientRemainingOwed -= deduct;
                    depositDeductedTotal += deduct;

                    if (dep.Amount - dep.UsedAmount - dep.RefundedAmount <= 0)
                    {
                        dep.Status = DepositStatus.Depleted;
                    }

                    _context.DepositUsages.Add(new DepositUsage
                    {
                        Deposit = dep,
                        Invoice = invoice,
                        Amount = deduct,
                        UsedAt = now,
                        CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null
                    });

                    int transSeqDep = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                    string depTransCode = $"PT-{transSeqDep:D8}";
                    if (string.IsNullOrEmpty(mainTransCode)) mainTransCode = depTransCode;

                    _context.Payments.Add(new Payment
                    {
                        Invoice = invoice,
                        Deposit = dep,
                        TransactionCode = depTransCode,
                        Amount = deduct,
                        PaymentType = PaymentType.Payment,
                        PaymentMethod = PaymentMethod.DepositDeduction,
                        Status = PaymentStatus.Confirmed,
                        CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                        ShiftId = shift?.Id,
                        Note = $"Cấn trừ tạm ứng {dep.DepositCode} vào quyết toán {invoice.InvoiceNumber}",
                        PaidAt = now
                    });
                }
            }

            // 2. Kiểm tra kết quả sau cấn trừ:
            // TH1: Bệnh nhân còn thiếu tiền -> Thu thêm (additionalPaid)
            if (patientRemainingOwed > 0)
            {
                if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
                {
                    return Result.Failure<SettleDischargeResultDto>("MISSING_REFERENCE", "Vui lòng nhập mã chuẩn chi / giao dịch ngân hàng.");
                }

                additionalPaid = patientRemainingOwed;
                int transSeqPay = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                string payTransCode = $"PT-{transSeqPay:D8}";
                mainTransCode = payTransCode;

                _context.Payments.Add(new Payment
                {
                    Invoice = invoice,
                    TransactionCode = payTransCode,
                    ReferenceCode = request.ReferenceCode?.Trim(),
                    Amount = additionalPaid,
                    PaymentType = PaymentType.Payment,
                    PaymentMethod = request.Method,
                    Status = PaymentStatus.Confirmed,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ShiftId = shift?.Id,
                    IdempotencyKey = request.IdempotencyKey,
                    Note = string.IsNullOrWhiteSpace(request.Note) ? $"Thu thêm chênh lệch quyết toán {invoice.InvoiceNumber}" : request.Note.Trim(),
                    PaidAt = now
                });
            }
            // TH2: Bệnh nhân còn dư tiền tạm ứng -> Hoàn trả (refundedToPatient)
            else
            {
                foreach (var dep in activeDeposits)
                {
                    decimal remainingDep = dep.Amount - dep.UsedAmount - dep.RefundedAmount;
                    if (remainingDep > 0)
                    {
                        dep.RefundedAmount += remainingDep;
                        dep.Status = DepositStatus.Refunded;
                        refundedToPatient += remainingDep;

                        int transSeqRef = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                        string refTransCode = $"PT-{transSeqRef:D8}";
                        if (string.IsNullOrEmpty(mainTransCode)) mainTransCode = refTransCode;

                        var refundNoteSuffix = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                            ? $" (Người duyệt hoàn: {request.ApprovedBy.Trim()})"
                            : "";
                        var refundReasonText = !string.IsNullOrWhiteSpace(request.Note)
                            ? request.Note.Trim()
                            : "Tất toán xuất viện và hoàn trả tiền mặt số dư tạm ứng";

                        _context.Payments.Add(new Payment
                        {
                            Invoice = invoice,
                            Deposit = dep,
                            TransactionCode = refTransCode,
                            ReferenceCode = null,
                            Amount = remainingDep,
                            PaymentType = PaymentType.Refund,
                            PaymentMethod = PaymentMethod.Cash,
                            Status = PaymentStatus.Confirmed,
                            CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                            ShiftId = shift?.Id,
                            RefundReason = refundReasonText,
                            Note = $"Hoàn tiền mặt số dư phiếu {dep.DepositCode} khi quyết toán {invoice.InvoiceNumber}{refundNoteSuffix}",
                            PaidAt = now
                        });
                    }
                }
            }

            if (string.IsNullOrEmpty(mainTransCode))
            {
                int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                mainTransCode = $"PT-{transSeq:D8}";
            }

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = app.PatientId,
                    Action = "SETTLE_DISCHARGE_INVOICE",
                    OccurredAt = now
                });
            }

            await _context.SaveChangesAsync(innerCt);

            var resultDto = new SettleDischargeResultDto(
                invoice.Id,
                invoice.InvoiceNumber,
                app.AppointmentCode,
                app.Patient.FullName,
                detail.TotalAmount,
                detail.InsuranceCoveredAmount,
                detail.PatientAmount,
                depositDeductedTotal,
                additionalPaid,
                refundedToPatient,
                request.Method,
                mainTransCode,
                now);

            return Result.Success(resultDto);
        }, ct);
    }

    public async Task<IReadOnlyList<LedgerItemDto>> GetLedgerAsync(LedgerFilterDto filter, bool exportAll = false, CancellationToken ct = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Invoice)!.ThenInclude(i => i!.Appointment)!.ThenInclude(a => a!.Patient)
            .Include(p => p.Deposit)!.ThenInclude(d => d!.Patient)
            .Include(p => p.Cashier)
            .Where(p =>
                (p.Invoice == null || p.Invoice.Appointment == null || !p.Invoice.Appointment.Patient.FullName.StartsWith("BN Test"))
                && (p.Deposit == null || !p.Deposit.Patient.FullName.StartsWith("BN Test")))
            .AsQueryable();

        if (filter.CashierId.HasValue)
        {
            query = query.Where(p => p.CashierId == filter.CashierId.Value);
        }

        if (filter.Method.HasValue)
        {
            query = query.Where(p => p.PaymentMethod == filter.Method.Value);
        }

        if (filter.Type.HasValue)
        {
            query = query.Where(p => p.PaymentType == filter.Type.Value);
        }

        if (filter.FromDate.HasValue)
        {
            var start = filter.FromDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(p => p.PaidAt >= start);
        }

        if (filter.ToDate.HasValue)
        {
            var end = filter.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(p => p.PaidAt < end);
        }

        var orderedQuery = query.OrderByDescending(p => p.PaidAt);
        var list = exportAll
            ? await orderedQuery.ToListAsync(ct)
            : await orderedQuery.Take(100).ToListAsync(ct);

        return list.Select(p => new LedgerItemDto(
            p.Id,
            p.TransactionCode ?? $"PT-{p.Id:D8}",
            p.PaidAt,
            p.Invoice?.InvoiceNumber ?? (p.Deposit != null ? p.Deposit.DepositCode : "—"),
            p.Invoice?.Appointment?.AppointmentCode ?? "—",
            p.Invoice?.Appointment?.Patient?.FullName ?? p.Deposit?.Patient?.FullName ?? "—",
            p.Amount,
            p.PaymentType,
            p.PaymentMethod,
            p.ReferenceCode,
            p.Cashier?.Username ?? "Hệ thống",
            p.ShiftId)).ToList();
    }

    public async Task<IReadOnlyList<PatientBillingHistoryItemDto>> GetPatientBillingHistoryAsync(int patientId, CancellationToken ct = default)
    {
        var invoices = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.Appointment)!.ThenInclude(a => a.Doctor)
            .Include(i => i.Payments)
            .Where(i => i.PatientId == patientId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        var result = new List<PatientBillingHistoryItemDto>();

        foreach (var inv in invoices)
        {
            var lastPayment = inv.Payments.OrderByDescending(p => p.PaidAt).FirstOrDefault();
            var examDate = inv.ExamDate ?? DateOnly.FromDateTime(inv.CreatedAt);

            result.Add(new PatientBillingHistoryItemDto(
                inv.Id,
                inv.InvoiceNumber ?? $"HD-{inv.Id:D6}",
                examDate,
                inv.Appointment?.AppointmentCode ?? "—",
                inv.Appointment?.Doctor?.FullName ?? "Bác sĩ",
                inv.TotalAmount,
                inv.InsuranceCoveredAmount,
                inv.PatientAmount,
                inv.Status,
                lastPayment?.PaidAt,
                lastPayment != null ? (lastPayment.PaymentMethod == PaymentMethod.Cash ? "Tiền mặt" : (lastPayment.PaymentMethod == PaymentMethod.BankTransfer ? "Chuyển khoản" : "Cấn trừ tạm ứng")) : null,
                lastPayment?.TransactionCode));
        }

        return result;
    }

    public async Task<Result<DepositDto>> CreateDepositAsync(CreateDepositRequest request, CancellationToken ct = default)
    {
        if (request.PatientId <= 0)
            return Result.Failure<DepositDto>("INVALID_PATIENT", "Mã bệnh nhân không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<DepositDto>("INVALID_AMOUNT", "Số tiền tạm ứng phải lớn hơn 0.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, innerCt);
            if (patient == null)
                return Result.Failure<DepositDto>("PATIENT_NOT_FOUND", "Không tìm thấy thông tin bệnh nhân.");

            var now = Now;
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<DepositDto>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thu tiền tạm ứng.");
            }

            int depSeq = await GetNextSequenceValueAsync("seq_deposit_code", innerCt);
            int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);

            string depCode = $"TU-{depSeq:D6}";
            string transCode = $"PT-{transSeq:D8}";

            int? targetAppointmentId = request.AppointmentId;
            Appointment? app = null;

            if (targetAppointmentId.HasValue)
            {
                app = await _context.Appointments
                    .Include(a => a.Doctor)
                    .FirstOrDefaultAsync(a => a.Id == targetAppointmentId.Value, innerCt);
            }
            else if (!patient.FullName.StartsWith("BN Test") && !patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"))
            {
                app = await _context.Appointments
                    .Include(a => a.Doctor)
                    .Include(a => a.Invoice)
                    .Where(a => a.PatientId == request.PatientId && (a.Invoice == null || a.Invoice.Status != InvoiceStatus.Paid))
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync(innerCt);

                if (app != null)
                {
                    if (app.StatusId < (int)AppointmentStatusId.CheckedIn)
                    {
                        app.StatusId = (int)AppointmentStatusId.CheckedIn;
                    }
                    targetAppointmentId = app.Id;
                }
                else
                {
                    var defaultDoc = await _context.Doctors.FirstOrDefaultAsync(innerCt);
                    var defaultDept = await _context.Departments.FirstOrDefaultAsync(innerCt);
                    if (defaultDoc != null && defaultDept != null)
                    {
                        app = new Appointment
                        {
                            PatientId = request.PatientId,
                            DoctorId = defaultDoc.Id,
                            DepartmentId = defaultDept.Id,
                            AppointmentCode = $"AP-{now:HHmmss}",
                            StatusId = (int)AppointmentStatusId.CheckedIn,
                            CreatedAt = now
                        };
                        _context.Appointments.Add(app);
                        await _context.SaveChangesAsync(innerCt);
                        targetAppointmentId = app.Id;
                    }
                }
            }

            string approvedByValue = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                ? request.ApprovedBy.Trim()
                : (app?.Doctor != null ? $"BS. {app.Doctor.FullName}" : "BS. Phạm Thu Hằng");

            var deposit = new Deposit
            {
                DepositCode = depCode,
                PatientId = request.PatientId,
                AppointmentId = targetAppointmentId,
                Amount = request.Amount,
                UsedAmount = 0m,
                RefundedAmount = 0m,
                Status = DepositStatus.Active,
                PaymentMethod = request.Method,
                ReferenceCode = request.ReferenceCode?.Trim(),
                Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "Tạm ứng viện phí" : request.Purpose.Trim(),
                ApprovedBy = approvedByValue,
                Note = request.Note?.Trim(),
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ShiftId = shift?.Id,
                CreatedAt = now
            };

            _context.Deposits.Add(deposit);
            await _context.SaveChangesAsync(innerCt);

            var payment = new Payment
            {
                DepositId = deposit.Id,
                InvoiceId = null,
                TransactionCode = transCode,
                ReferenceCode = request.ReferenceCode?.Trim(),
                Amount = request.Amount,
                PaymentType = PaymentType.Deposit,
                PaymentMethod = request.Method,
                Status = PaymentStatus.Confirmed,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ShiftId = shift?.Id,
                Note = $"Thu tạm ứng {depCode}: {deposit.Purpose} (Duyệt: {approvedByValue})",
                PaidAt = now
            };

            _context.Payments.Add(payment);

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = request.PatientId,
                    Action = "CREATE_DEPOSIT",
                    OccurredAt = now
                });
            }

            await _context.SaveChangesAsync(innerCt);

            var cashier = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == request.CashierAccountId, innerCt);

            var dto = new DepositDto(
                deposit.Id,
                deposit.DepositCode,
                deposit.PatientId,
                deposit.PatientId.ToString("D8"),
                patient.FullName,
                patient.PhoneNumber,
                deposit.AppointmentId,
                app?.AppointmentCode,
                deposit.Amount,
                0m,
                0m,
                deposit.Amount,
                deposit.Status,
                deposit.PaymentMethod,
                deposit.ReferenceCode,
                deposit.Purpose,
                deposit.Note,
                cashier?.Username ?? "Hệ thống",
                deposit.CreatedAt,
                patient.NationalId,
                deposit.ApprovedBy);

            return Result.Success(dto);
        }, ct);
    }

    public async Task<PatientDepositSummaryDto> GetPatientDepositSummaryAsync(int patientId, CancellationToken ct = default)
    {
        var deposits = await _context.Deposits
            .AsNoTracking()
            .Include(d => d.Cashier)
            .Include(d => d.Appointment)
            .Include(d => d.Patient)
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        decimal totalDeposited = deposits.Sum(d => d.Amount);
        decimal totalUsed = deposits.Sum(d => d.UsedAmount);
        decimal totalRefunded = deposits.Sum(d => d.RefundedAmount);
        decimal available = totalDeposited - totalUsed - totalRefunded;

        var dtos = deposits.Select(d => new DepositDto(
            d.Id,
            d.DepositCode,
            d.PatientId,
            d.PatientId.ToString("D8"),
            d.Patient.FullName,
            d.Patient.PhoneNumber,
            d.AppointmentId,
            d.Appointment?.AppointmentCode,
            d.Amount,
            d.UsedAmount,
            d.RefundedAmount,
            d.Amount - d.UsedAmount - d.RefundedAmount,
            d.Status,
            d.PaymentMethod,
            d.ReferenceCode,
            d.Purpose,
            d.Note,
            d.Cashier?.Username ?? "Hệ thống",
            d.CreatedAt,
            d.Patient.NationalId,
            d.ApprovedBy)).ToList();

        return new PatientDepositSummaryDto(patientId, totalDeposited, totalUsed, totalRefunded, available, dtos);
    }

    public async Task<IReadOnlyList<DepositDto>> GetDepositsAsync(
        string? search = null,
        PaymentMethod? method = null,
        DepositStatus? status = null,
        int? patientId = null,
        CancellationToken ct = default)
    {
        var query = _context.Deposits
            .AsNoTracking()
            .Include(d => d.Patient)
            .Include(d => d.Appointment)
            .Include(d => d.Cashier)
            .AsQueryable();

        if (patientId.HasValue)
            query = query.Where(d => d.PatientId == patientId.Value);

        if (status.HasValue)
            query = query.Where(d => d.Status == status.Value);

        if (method.HasValue)
            query = query.Where(d => d.PaymentMethod == method.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            bool hasIntId = TryParsePatientId(search, out var parsedId);
            query = query.Where(d =>
                d.DepositCode.Contains(search)
                || d.Patient.FullName.Contains(search)
                || d.Patient.PhoneNumber.Contains(search)
                || (d.Patient.NationalId != null && d.Patient.NationalId.Contains(search))
                || (hasIntId && d.PatientId == parsedId)
                || (d.ApprovedBy != null && d.ApprovedBy.Contains(search))
                || (d.Appointment != null && d.Appointment.AppointmentCode.Contains(search))
                || (d.ReferenceCode != null && d.ReferenceCode.Contains(search)));
        }
        else if (!patientId.HasValue)
        {
            query = query.Where(d => !d.Patient.FullName.StartsWith("BN Test") && !d.Patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"));
        }

        var list = await query.OrderByDescending(d => d.CreatedAt).Take(150).ToListAsync(ct);

        return list.Select(d => new DepositDto(
            d.Id,
            d.DepositCode,
            d.PatientId,
            d.PatientId.ToString("D8"),
            d.Patient.FullName,
            d.Patient.PhoneNumber,
            d.AppointmentId,
            d.Appointment?.AppointmentCode,
            d.Amount,
            d.UsedAmount,
            d.RefundedAmount,
            d.Amount - d.UsedAmount - d.RefundedAmount,
            d.Status,
            d.PaymentMethod,
            d.ReferenceCode,
            d.Purpose,
            d.Note,
            d.Cashier?.Username ?? "Hệ thống",
            d.CreatedAt,
            d.Patient.NationalId,
            d.ApprovedBy)).ToList();
    }

    public async Task<DepositSummaryTotalsDto> GetDepositTotalsAsync(CancellationToken ct = default)
    {
        var agg = await _context.Deposits
            .AsNoTracking()
            .Where(d => !d.Patient.FullName.StartsWith("BN Test") && !d.Patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"))
            .GroupBy(_ => 1)
            .Select(g => new DepositSummaryTotalsDto(
                g.Count(),
                g.Sum(d => d.Amount),
                g.Sum(d => d.UsedAmount),
                g.Sum(d => d.Amount - d.UsedAmount - d.RefundedAmount)))
            .FirstOrDefaultAsync(ct);

        return agg ?? new DepositSummaryTotalsDto(0, 0m, 0m, 0m);
    }

    public async Task<Result<string>> RefundDepositAsync(RefundDepositRequest request, CancellationToken ct = default)
    {
        if (request.DepositId <= 0)
            return Result.Failure<string>("INVALID_DEPOSIT", "Mã phiếu tạm ứng không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<string>("INVALID_AMOUNT", "Số tiền hoàn phải lớn hơn 0.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<string>("MISSING_REASON", "Bắt buộc phải nhập lý do hoàn tiền tạm ứng.");

        if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
            return Result.Failure<string>("MISSING_REFERENCE", "Vui lòng nhập mã giao dịch ngân hàng khi hoàn tiền qua chuyển khoản.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var deposit = await _context.Deposits
                .FromSqlInterpolated($"SELECT * FROM deposits WHERE id = {request.DepositId} FOR UPDATE")
                .Include(d => d.Patient)
                .FirstOrDefaultAsync(innerCt);

            if (deposit == null)
                return Result.Failure<string>("NOT_FOUND", "Không tìm thấy phiếu tạm ứng.");

            decimal available = deposit.Amount - deposit.UsedAmount - deposit.RefundedAmount;
            if (request.Amount > available)
                return Result.Failure<string>("AMOUNT_EXCEEDED", $"Số tiền yêu cầu hoàn ({request.Amount:N0} đ) vượt quá số dư tạm ứng còn lại ({available:N0} đ).");

            var now = Now;
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<string>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện hoàn tiền tạm ứng.");
            }

            deposit.RefundedAmount += request.Amount;
            if (deposit.Amount - deposit.UsedAmount - deposit.RefundedAmount <= 0)
            {
                deposit.Status = DepositStatus.Refunded;
            }

            int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
            string transCode = $"PT-{transSeq:D8}";

            var payment = new Payment
            {
                Deposit = deposit,
                InvoiceId = null,
                TransactionCode = transCode,
                ReferenceCode = request.ReferenceCode?.Trim(),
                Amount = request.Amount,
                PaymentType = PaymentType.Refund,
                PaymentMethod = request.Method,
                Status = PaymentStatus.Confirmed,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ShiftId = shift?.Id,
                RefundReason = request.Reason.Trim(),
                Note = $"Hoàn tiền tạm ứng {deposit.DepositCode}: {request.Reason.Trim()}",
                PaidAt = now
            };

            _context.Payments.Add(payment);

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = deposit.PatientId,
                    Action = "REFUND_DEPOSIT",
                    OccurredAt = now
                });
            }

            await _context.SaveChangesAsync(innerCt);
            return Result.Success(transCode);
        }, ct);
    }

    public async Task<Result<QuickPatientDepositLookupDto>> QuickLookupPatientDepositsAsync(string search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Result.Failure<QuickPatientDepositLookupDto>("INVALID_SEARCH", "Vui lòng nhập Mã BN, số CCCD hoặc SĐT.");
        }

        search = search.Trim();
        bool hasIntId = TryParsePatientId(search, out var parsedId);

        var patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                (hasIntId && p.Id == parsedId)
                || p.NationalId == search
                || p.PhoneNumber == search
                || p.FullName.Contains(search), ct);

        if (patient == null)
        {
            return Result.Failure<QuickPatientDepositLookupDto>("PATIENT_NOT_FOUND", "Không tìm thấy thông tin bệnh nhân phù hợp.");
        }

        var deposits = await _context.Deposits
            .AsNoTracking()
            .Include(d => d.Cashier)
            .Include(d => d.Appointment)
            .Where(d => d.PatientId == patient.Id)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var activeSlips = deposits
            .Where(d => d.Status == DepositStatus.Active && (d.Amount - d.UsedAmount - d.RefundedAmount) > 0)
            .Select(d => new DepositDto(
                d.Id,
                d.DepositCode,
                d.PatientId,
                d.PatientId.ToString("D8"),
                patient.FullName,
                patient.PhoneNumber,
                d.AppointmentId,
                d.Appointment?.AppointmentCode,
                d.Amount,
                d.UsedAmount,
                d.RefundedAmount,
                d.Amount - d.UsedAmount - d.RefundedAmount,
                d.Status,
                d.PaymentMethod,
                d.ReferenceCode,
                d.Purpose,
                d.Note,
                d.Cashier?.Username ?? "Hệ thống",
                d.CreatedAt,
                patient.NationalId,
                d.ApprovedBy)).ToList();

        decimal totalDeposited = activeSlips.Sum(s => s.Amount);
        decimal totalUsed = activeSlips.Sum(s => s.UsedAmount);
        decimal totalRefunded = 0m;
        decimal available = activeSlips.Sum(s => s.Balance);

        decimal cashBalance = activeSlips.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Balance);
        decimal bankBalance = activeSlips.Where(s => s.PaymentMethod == PaymentMethod.BankTransfer).Sum(s => s.Balance);

        var allSlips = activeSlips;

        // Lấy thông tin lượt khám & bảng kê chi tiết viện phí (dịch vụ, CLS, thuốc)
        var latestApp = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.InvoiceItems)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .Where(a => a.PatientId == patient.Id)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var serviceItems = new List<InvoiceItemDto>();
        decimal totalInvoiceAmount = 0m;
        decimal totalInsurancePaid = 0m;
        decimal totalPatientPayable = 0m;
        string? insuranceCardNum = null;
        decimal baseCoveragePct = 0m;

        if (latestApp != null)
        {
            var examDate = latestApp.Slot != null ? DateOnly.FromDateTime(latestApp.Slot.StartTime) : DateOnly.FromDateTime(latestApp.CreatedAt);
            var insuranceCard = await _context.InsuranceCards
                .AsNoTracking()
                .Include(c => c.InsurancePlan)
                .FirstOrDefaultAsync(c => c.PatientId == patient.Id && c.IsActive && c.ValidFrom <= examDate && c.ValidTo >= examDate, ct);

            bool hasInsurance = insuranceCard != null;
            if (hasInsurance && insuranceCard != null)
            {
                insuranceCardNum = insuranceCard.CardNumber;
                bool isRightRoute = insuranceCard.IsRegisteredHere || insuranceCard.InsurancePlan?.ReferralType == ReferralType.InNetwork;
                baseCoveragePct = isRightRoute ? (insuranceCard.InsurancePlan?.CoveragePct ?? 80m) : _options.OutOfRoutePct;
            }

            int itemId = 1;

            // 1. Phí công khám
            var examService = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _options.ExamFeeServiceId, ct);
            decimal examUnitPrice = examService?.Price ?? _options.DefaultExamPrice;
            bool examCovered = examService?.IsInsuranceCovered ?? true;
            decimal examLineCoverage = (hasInsurance && examCovered) ? baseCoveragePct : 0m;
            decimal examCoveredAmount = Math.Round(examUnitPrice * examLineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
            decimal examPatientAmount = examUnitPrice - examCoveredAmount;

            serviceItems.Add(new InvoiceItemDto(
                itemId++,
                "Công khám",
                examService?.Name ?? "Khám chuyên khoa",
                examUnitPrice,
                1,
                examUnitPrice,
                examCovered,
                examLineCoverage,
                examCoveredAmount,
                examPatientAmount));

            // 2. Cận lâm sàng / Xét nghiệm
            if (latestApp.MedicalRecord?.LabOrders != null)
            {
                foreach (var order in latestApp.MedicalRecord.LabOrders)
                {
                    if (order.Service != null)
                    {
                        decimal unitPrice = order.Service.Price;
                        bool isCovered = order.Service.IsInsuranceCovered;
                        decimal lineCoverage = (hasInsurance && isCovered) ? baseCoveragePct : 0m;
                        decimal coveredAmount = Math.Round(unitPrice * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                        decimal patientAmount = unitPrice - coveredAmount;

                        serviceItems.Add(new InvoiceItemDto(
                            itemId++,
                            order.Service.ServiceType?.Name ?? "Cận lâm sàng",
                            order.Service.Name,
                            unitPrice,
                            1,
                            unitPrice,
                            isCovered,
                            lineCoverage,
                            coveredAmount,
                            patientAmount));
                    }
                }
            }

            // 3. Thuốc theo đơn
            if (latestApp.MedicalRecord?.Prescriptions != null)
            {
                foreach (var p in latestApp.MedicalRecord.Prescriptions)
                {
                    foreach (var pi in p.PrescriptionItems)
                    {
                        if (pi.Medication != null)
                        {
                            decimal unitPrice = pi.Medication.Price;
                            int qty = pi.Quantity;
                            decimal total = unitPrice * qty;
                            bool isCovered = pi.Medication.IsInsuranceCovered;
                            decimal lineCoverage = (hasInsurance && isCovered) ? baseCoveragePct : 0m;
                            decimal coveredAmount = Math.Round(total * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                            decimal patientAmount = total - coveredAmount;

                            serviceItems.Add(new InvoiceItemDto(
                                itemId++,
                                "Thuốc điều trị",
                                $"{pi.Medication.Name} {(string.IsNullOrWhiteSpace(pi.Medication.Strength) ? "" : $"({pi.Medication.Strength})")}".Trim(),
                                unitPrice,
                                qty,
                                total,
                                isCovered,
                                lineCoverage,
                                coveredAmount,
                                patientAmount));
                        }
                    }
                }
            }

            totalInvoiceAmount = serviceItems.Sum(i => i.Total);
            totalInsurancePaid = serviceItems.Sum(i => i.CoveredAmount);
            totalPatientPayable = serviceItems.Sum(i => i.PatientAmount);
        }

        var dto = new QuickPatientDepositLookupDto(
            patient.Id,
            patient.Id.ToString("D8"),
            patient.FullName,
            patient.NationalId,
            patient.PhoneNumber,
            totalDeposited,
            totalUsed,
            totalRefunded,
            available,
            activeSlips.Count,
            cashBalance,
            bankBalance,
            activeSlips,
            insuranceCardNum,
            baseCoveragePct,
            latestApp?.AppointmentCode,
            latestApp?.Department?.Name,
            latestApp?.Doctor?.FullName,
            latestApp?.CreatedAt,
            totalInvoiceAmount,
            totalInsurancePaid,
            totalPatientPayable,
            serviceItems,
            allSlips);

        return Result.Success(dto);
    }

    public async Task<Result<RefundAllDepositsResultDto>> RefundAllPatientDepositsAsync(RefundAllPatientDepositsRequest request, CancellationToken ct = default)
    {
        if (request.PatientId <= 0)
            return Result.Failure<RefundAllDepositsResultDto>("INVALID_PATIENT", "Mã bệnh nhân không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<RefundAllDepositsResultDto>("MISSING_REASON", "Bắt buộc phải nhập lý do tất toán & hoàn tiền.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, innerCt);
            if (patient == null)
                return Result.Failure<RefundAllDepositsResultDto>("PATIENT_NOT_FOUND", "Không tìm thấy thông tin bệnh nhân.");

            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<RefundAllDepositsResultDto>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện hoàn tiền.");
            }

            var activeDeposits = await _context.Deposits
                .FromSqlInterpolated($"SELECT * FROM deposits WHERE patient_id = {request.PatientId} AND status = 'ACTIVE' AND (amount - used_amount - refunded_amount) > 0 ORDER BY created_at FOR UPDATE")
                .ToListAsync(innerCt);

            if (activeDeposits.Count == 0)
            {
                return Result.Failure<RefundAllDepositsResultDto>("NO_BALANCE", "Bệnh nhân không có số dư tạm ứng nào cần hoàn trả.");
            }

            bool hasBankTransfer = activeDeposits.Any(d => d.PaymentMethod == PaymentMethod.BankTransfer);
            if (hasBankTransfer && string.IsNullOrWhiteSpace(request.BankReferenceCode))
            {
                return Result.Failure<RefundAllDepositsResultDto>("MISSING_BANK_REF", "Bệnh nhân có khoản tạm ứng qua chuyển khoản. Vui lòng nhập mã chuẩn chi / giao dịch ngân hàng.");
            }

            var now = Now;
            decimal totalRefunded = 0m;
            decimal cashRefunded = 0m;
            decimal bankRefunded = 0m;
            var transCodes = new List<string>();

            foreach (var deposit in activeDeposits)
            {
                decimal balance = deposit.Amount - deposit.UsedAmount - deposit.RefundedAmount;
                if (balance <= 0) continue;

                deposit.RefundedAmount += balance;
                deposit.Status = DepositStatus.Refunded;

                int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
                string transCode = $"PT-{transSeq:D8}";
                transCodes.Add(transCode);

                var payment = new Payment
                {
                    Deposit = deposit,
                    InvoiceId = null,
                    TransactionCode = transCode,
                    ReferenceCode = deposit.PaymentMethod == PaymentMethod.BankTransfer ? request.BankReferenceCode?.Trim() : null,
                    Amount = balance,
                    PaymentType = PaymentType.Refund,
                    PaymentMethod = deposit.PaymentMethod,
                    Status = PaymentStatus.Confirmed,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ShiftId = shift.Id,
                    RefundReason = request.Reason.Trim(),
                    Note = $"Tất toán hoàn trả số dư phiếu {deposit.DepositCode}: {request.Reason.Trim()}",
                    PaidAt = now
                };

                _context.Payments.Add(payment);

                totalRefunded += balance;
                if (deposit.PaymentMethod == PaymentMethod.Cash)
                    cashRefunded += balance;
                else
                    bankRefunded += balance;
            }

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = patient.Id,
                    Action = "BATCH_REFUND_ALL_DEPOSITS",
                    OccurredAt = now
                });
            }

            await _context.SaveChangesAsync(innerCt);

            var resultDto = new RefundAllDepositsResultDto(
                patient.Id,
                patient.FullName,
                totalRefunded,
                activeDeposits.Count,
                cashRefunded,
                bankRefunded,
                transCodes);

            return Result.Success(resultDto);
        }, ct);
    }

    public async Task<Result<string>> RefundInvoiceAsync(RefundInvoiceRequest request, CancellationToken ct = default)
    {
        if (request.InvoiceId <= 0)
            return Result.Failure<string>("INVALID_INVOICE", "Mã hóa đơn không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<string>("INVALID_AMOUNT", "Số tiền hoàn phải lớn hơn 0.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<string>("MISSING_REASON", "Bắt buộc phải nhập lý do hoàn tiền viện phí.");

        if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
            return Result.Failure<string>("MISSING_REFERENCE", "Vui lòng nhập mã giao dịch ngân hàng khi hoàn tiền qua chuyển khoản.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var invoice = await _context.Invoices
                .FromSqlInterpolated($"SELECT * FROM invoices WHERE id = {request.InvoiceId} FOR UPDATE")
                .Include(i => i.Payments)
                .Include(i => i.Appointment)!.ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(innerCt);

            if (invoice == null)
                return Result.Failure<string>("NOT_FOUND", "Không tìm thấy hóa đơn viện phí.");

            decimal totalRefunded = invoice.Payments.Where(p => p.PaymentType == PaymentType.Refund).Sum(p => p.Amount);
            decimal maxRefundable = invoice.PatientAmount - totalRefunded;

            if (request.Amount > maxRefundable)
                return Result.Failure<string>("AMOUNT_EXCEEDED", $"Số tiền hoàn ({request.Amount:N0} đ) vượt quá số tiền bệnh nhân có thể hoàn (tối đa {maxRefundable:N0} đ).");

            var now = Now;
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);

            if (shift == null)
            {
                return Result.Failure<string>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện hoàn tiền viện phí.");
            }

            int transSeq = await GetNextSequenceValueAsync("seq_transaction_code", innerCt);
            string transCode = $"PT-{transSeq:D8}";

            var approverSuffix = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                ? $" (Người duyệt: {request.ApprovedBy.Trim()})"
                : "";

            var payment = new Payment
            {
                Invoice = invoice,
                TransactionCode = transCode,
                ReferenceCode = null,
                Amount = request.Amount,
                PaymentType = PaymentType.Refund,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Confirmed,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ShiftId = shift?.Id,
                RefundReason = request.Reason.Trim(),
                Note = $"Hoàn tiền mặt HĐ {invoice.InvoiceNumber}: {request.Reason.Trim()}{approverSuffix}",
                PaidAt = now
            };

            _context.Payments.Add(payment);

            if (request.CashierAccountId > 0 && invoice.PatientId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = invoice.PatientId,
                    Action = "REFUND_INVOICE",
                    OccurredAt = now
                });
            }

            await _context.SaveChangesAsync(innerCt);
            return Result.Success(transCode);
        }, ct);
    }

    public async Task<IReadOnlyList<PatientSearchResultDto>> SearchPatientsAsync(string search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Array.Empty<PatientSearchResultDto>();
        }

        search = search.Trim();
        bool hasIntId = TryParsePatientId(search, out var parsedId);

        var query = _context.Patients
            .AsNoTracking()
            .Include(p => p.Appointments)
                .ThenInclude(a => a.Department)
            .Where(p =>
                (hasIntId && p.Id == parsedId)
                || (p.NationalId != null && p.NationalId.Contains(search))
                || p.PhoneNumber.Contains(search)
                || p.FullName.Contains(search));

        var patients = await query
            .OrderBy(p => p.FullName)
            .Take(15)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);

        return patients.Select(p =>
        {
            var latestApp = p.Appointments
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            int age = 0;
            if (p.DateOfBirth != default && p.DateOfBirth.Year > 1900)
            {
                age = today.Year - p.DateOfBirth.Year;
                if (p.DateOfBirth > today.AddYears(-age)) age--;
            }

            string genderStr = p.Gender switch
            {
                Gender.Male => "Nam",
                Gender.Female => "Nữ",
                _ => "Khác"
            };

            return new PatientSearchResultDto(
                p.Id,
                p.Id.ToString("D8"),
                p.FullName,
                p.NationalId,
                p.PhoneNumber,
                p.DateOfBirth,
                age,
                genderStr,
                p.Address,
                latestApp?.Id,
                latestApp?.AppointmentCode,
                latestApp?.Department?.Name);
        }).ToList();
    }

    private static bool TryParsePatientId(string search, out int parsedId)
    {
        parsedId = 0;
        if (string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        var s = search.Trim();
        if (s.StartsWith("BN", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..].TrimStart('-', ' ');
        }

        var trimmed = s.TrimStart('0');
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        return int.TryParse(trimmed, out parsedId) && parsedId > 0;
    }

    private async Task<int> GetNextSequenceValueAsync(string sequenceName, CancellationToken ct)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT nextval('{sequenceName}')";
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }
}
