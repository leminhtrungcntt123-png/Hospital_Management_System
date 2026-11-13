using System.Globalization;
using System.Text.Json;
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
    private const string InvoiceEntityName = "invoices";
    private const string ActionSettleInvoice = "SETTLE_INVOICE";

    private sealed record DepositMeta(decimal Used, decimal Refunded, string? ReferenceCode);
    private sealed record DepositNoteMeta(string ApprovedBy, string Purpose, string? UserNote);
    private sealed record InvoiceSettlementMeta(decimal DepositDeducted, string? IdempotencyKey, string? MainTransactionCode);

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

    private Task<AccessLog?> LockOpenShiftAsync(int cashierAccountId, CancellationToken ct)
    {
        if (cashierAccountId <= 0)
        {
            return Task.FromResult<AccessLog?>(null);
        }

        if (_context.Database.IsNpgsql())
        {
            return _context.AccessLogs
                .FromSqlInterpolated($"SELECT * FROM access_logs WHERE entity_name = {CashierShiftService.ShiftEntityName} AND account_id = {cashierAccountId} AND action = {CashierShiftService.ActionOpen} ORDER BY occurred_at DESC LIMIT 1 FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        return _context.AccessLogs
            .Where(l => l.EntityName == CashierShiftService.ShiftEntityName
                && l.AccountId == cashierAccountId
                && l.Action == CashierShiftService.ActionOpen)
            .OrderByDescending(l => l.OccurredAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Result<int>> GenerateInvoiceAsync(int appointmentId, int? insuranceCardId, CancellationToken ct = default)
    {
        if (appointmentId <= 0)
        {
            return Result.Failure<int>("INVALID_REQUEST", "Mã lượt khám không hợp lệ.");
        }

        var existingInvoiceId = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.AppointmentId == appointmentId)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(ct);

        if (existingInvoiceId.HasValue)
        {
            return Result.Success(existingInvoiceId.Value);
        }

        var detailRes = await GetInvoiceDetailAsync(appointmentId, ct);
        if (!detailRes.IsSuccess)
        {
            return Result.Failure<int>(detailRes.Error.Code, detailRes.Error.Message);
        }

        var detail = detailRes.Value!;
        var card = insuranceCardId.HasValue
            ? await _context.InsuranceCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == insuranceCardId.Value && c.PatientId == detail.PatientId && c.IsActive, ct)
            : await _context.InsuranceCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.PatientId == detail.PatientId && c.IsActive && c.ValidFrom <= detail.ExamDate && c.ValidTo >= detail.ExamDate, ct);

        var invoice = new Invoice
        {
            AppointmentId = detail.AppointmentId,
            PatientId = detail.PatientId,
            InsuranceCardId = card?.Id,
            InsurancePlanId = card?.InsurancePlanId,
            CoveragePctApplied = detail.CoveragePct,
            TotalAmount = detail.TotalAmount,
            InsuranceCoveredAmount = detail.InsuranceCoveredAmount,
            PatientAmount = detail.PatientAmount,
            Status = InvoiceStatus.Unpaid,
            CreatedAt = Now
        };

        foreach (var item in detail.Items)
        {
            invoice.InvoiceItems.Add(new InvoiceItem
            {
                Category = MapCategory(item.Category),
                Description = Truncate(item.Description, 255)!,
                Amount = item.Total
            });
        }

        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(ct);
        return Result.Success(invoice.Id);
    }

    public async Task<Result<int>> ProcessPaymentAsync(int invoiceId, decimal amount, string paymentMethod, string? receiptEmail, int? cashierId, CancellationToken ct = default)
    {
        if (invoiceId <= 0 || amount <= 0)
        {
            return Result.Failure<int>("INVALID_REQUEST", "Thông tin thanh toán không hợp lệ.");
        }

        var invoice = await _context.Invoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice == null)
        {
            return Result.Failure<int>("NOT_FOUND", "Không tìm thấy hóa đơn.");
        }

        var method = paymentMethod.Equals("BANK_TRANSFER", StringComparison.OrdinalIgnoreCase)
            || paymentMethod.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase)
            ? PaymentMethod.BankTransfer
            : PaymentMethod.Cash;

        var payment = new Payment
        {
            InvoiceId = invoice.Id,
            Amount = amount,
            PaymentType = PaymentType.Payment,
            PaymentMethod = method,
            CashierId = cashierId,
            ReceiptEmail = Truncate(receiptEmail?.Trim(), 100),
            Note = "Thanh toán viện phí",
            PaidAt = Now
        };

        _context.Payments.Add(payment);
        decimal totalPaid = invoice.Payments.Where(p => p.PaymentType == PaymentType.Payment).Sum(p => p.Amount) + amount;
        invoice.Status = totalPaid >= invoice.PatientAmount ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;

        await _context.SaveChangesAsync(ct);
        return Result.Success(payment.Id);
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
            .Where(a => a.StatusId >= (int)AppointmentStatusId.CheckedIn
                && (!a.AppointmentCode.StartsWith("DEP-") || (a.Invoice != null && a.Invoice.TotalAmount > 0)));

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

        var depositPayments = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Invoice)
            .Where(p => p.PaymentType == PaymentType.Deposit && patientIds.Contains(p.Invoice.PatientId))
            .ToListAsync(ct);

        var patientDeposits = depositPayments
            .GroupBy(p => p.Invoice.PatientId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(p =>
                {
                    var meta = ParseDepositState(p.ReceiptEmail);
                    return Math.Max(0m, p.Amount - meta.Used - meta.Refunded);
                }));

        var examService = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _options.ExamFeeServiceId, ct);
        var list = new List<PendingInvoiceItemDto>();

        foreach (var a in appointments)
        {
            var examDate = a.Slot != null ? DateOnly.FromDateTime(a.Slot.StartTime) : DateOnly.FromDateTime(a.CreatedAt);
            var itemInvoiceStatus = a.Invoice?.Status ?? InvoiceStatus.Unpaid;

            var card = insuranceCards.FirstOrDefault(c => c.PatientId == a.PatientId && c.ValidFrom <= examDate && c.ValidTo >= examDate);
            bool hasInsurance = card != null;
            decimal baseCoveragePct = GetBaseCoveragePct(card);

            decimal totalAmount;
            decimal patientPayAmount;

            if (a.Invoice != null)
            {
                totalAmount = a.Invoice.TotalAmount;
                patientPayAmount = a.Invoice.PatientAmount;
            }
            else
            {
                var (_, calcTotal, _, calcPatient) = BuildInvoiceLineItems(a, examService, hasInsurance, baseCoveragePct);
                totalAmount = calcTotal;
                patientPayAmount = calcPatient;
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
                a.Invoice != null ? $"HD-{a.Invoice.Id:D6}" : null,
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

        var insuranceCard = await _context.InsuranceCards
            .AsNoTracking()
            .Include(c => c.InsurancePlan)
            .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= examDate && c.ValidTo >= examDate, ct);

        bool hasInsurance = insuranceCard != null;
        decimal baseCoveragePct = GetBaseCoveragePct(insuranceCard);
        string? cardNum = insuranceCard?.CardNumber;
        string? facility = insuranceCard?.RegisteredFacility;

        var examService = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _options.ExamFeeServiceId, ct);
        var (items, totalAmount, insuranceCoveredAmount, patientAmountTotal) =
            BuildInvoiceLineItems(app, examService, hasInsurance, baseCoveragePct);

        var invoice = app.Invoice;
        var allPayments = invoice?.Payments
            .Where(p => p.PaymentType != PaymentType.Deposit)
            .OrderBy(p => p.PaidAt)
            .ToList() ?? new List<Payment>();
        var payment = allPayments.LastOrDefault(p => p.PaymentType == PaymentType.Payment) ?? allPayments.LastOrDefault();

        decimal additionalPaid = allPayments.Where(p => p.PaymentType == PaymentType.Payment).Sum(p => p.Amount);
        decimal refundedAmount = allPayments.Where(p => p.PaymentType == PaymentType.Refund).Sum(p => p.Amount);
        decimal depositDeducted = invoice != null ? await GetRecordedDepositDeductedAsync(invoice.Id, ct) : 0m;

        string? paymentMethodDisplay = null;
        if (depositDeducted > 0 || allPayments.Count > 0)
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
                paymentMethodDisplay = payment.PaymentMethod == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản";
            }
        }

        string? transactionCodesDisplay = allPayments.Count > 0
            ? string.Join(", ", allPayments.Select(p => $"PT-{p.Id:D8}").Distinct())
            : null;

        int age = examDate.Year - app.Patient.DateOfBirth.Year;
        string genderStr = app.Patient.Gender switch
        {
            Gender.Male => "Nam",
            Gender.Female => "Nữ",
            _ => "Khác"
        };

        var patientDepositPayments = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Appointment)
            .Include(p => p.Cashier)
            .Where(p => p.PaymentType == PaymentType.Deposit && p.Invoice.PatientId == app.PatientId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(ct);

        var allDepositDtos = patientDepositPayments.Select(MapPaymentToDepositDto).ToList();
        var activeDeposits = allDepositDtos.Where(d => d.Status == DepositStatus.Active && d.Balance > 0).ToList();
        decimal availableDepositBalance = activeDeposits.Sum(d => d.Balance);
        var displayDeposits = activeDeposits.Count == 0 && invoice?.Status == InvoiceStatus.Paid
            ? allDepositDtos
            : activeDeposits;

        string? diagnosisText = app.MedicalRecord != null && !string.IsNullOrWhiteSpace(app.MedicalRecord.PrimaryDiagnosisDesc)
            ? (!string.IsNullOrWhiteSpace(app.MedicalRecord.PrimaryDiagnosisCode)
                ? $"[{app.MedicalRecord.PrimaryDiagnosisCode}] {app.MedicalRecord.PrimaryDiagnosisDesc}"
                : app.MedicalRecord.PrimaryDiagnosisDesc)
            : "Khám và theo dõi chuyên khoa";

        var detail = new InvoiceDetailDto(
            invoice?.Id ?? 0,
            invoice != null ? $"HD-{invoice.Id:D6}" : "Chưa lập",
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
            payment?.PaidAt ?? (invoice?.Status == InvoiceStatus.Paid ? invoice.CreatedAt : null),
            paymentMethodDisplay,
            transactionCodesDisplay,
            payment?.Cashier?.Username,
            availableDepositBalance,
            displayDeposits,
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
        if (request.AppointmentId <= 0 || !Enum.IsDefined(request.Method))
        {
            return Result.Failure<int>("INVALID_REQUEST", "Thông tin yêu cầu thanh toán không hợp lệ.");
        }

        if (request.DepositDeductAmount.HasValue && request.DepositDeductAmount.Value < 0)
        {
            return Result.Failure<int>("INVALID_AMOUNT", "Số tiền cấn trừ tạm ứng không được âm.");
        }

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingInvoiceId = await FindInvoiceIdByIdempotencyKeyAsync(request.IdempotencyKey, innerCt);
                if (existingInvoiceId.HasValue)
                {
                    return Result.Success(existingInvoiceId.Value);
                }
            }

            var app = await LockAppointmentAsync(request.AppointmentId, innerCt);
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

            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);
            if (shift == null)
            {
                return Result.Failure<int>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện thu tiền.");
            }

            var invoice = UpsertPaidInvoice(app, detail, insuranceCard, now);

            decimal depositDeduct = 0m;
            if (request.DepositId.HasValue && request.DepositDeductAmount.HasValue && request.DepositDeductAmount.Value > 0)
            {
                var depositPayment = await LockDepositPaymentByIdAsync(request.DepositId.Value, innerCt);
                if (depositPayment == null || depositPayment.PaymentType != PaymentType.Deposit)
                {
                    return Result.Failure<int>("DEPOSIT_NOT_FOUND", "Không tìm thấy thông tin phiếu tạm ứng hợp lệ.");
                }

                if (depositPayment.Invoice.PatientId != app.PatientId)
                {
                    return Result.Failure<int>("DEPOSIT_MISMATCH", "Phiếu tạm ứng không thuộc về bệnh nhân này.");
                }

                var depMeta = ParseDepositState(depositPayment.ReceiptEmail);
                decimal maxAvailable = depositPayment.Amount - depMeta.Used - depMeta.Refunded;
                if (maxAvailable <= 0)
                {
                    return Result.Failure<int>("DEPOSIT_NOT_FOUND", "Không tìm thấy thông tin phiếu tạm ứng hợp lệ.");
                }

                if (request.DepositDeductAmount.Value > maxAvailable)
                {
                    return Result.Failure<int>("INSUFFICIENT_DEPOSIT", $"Số tiền trừ ({request.DepositDeductAmount.Value:N0} đ) vượt quá số dư tạm ứng còn lại ({maxAvailable:N0} đ).");
                }

                depositDeduct = Math.Min(detail.PatientAmount, request.DepositDeductAmount.Value);
                depositPayment.ReceiptEmail = BuildDepositState(depMeta.Used + depositDeduct, depMeta.Refunded, depMeta.ReferenceCode);
            }

            Payment? addedPayment = null;
            decimal remainingToPay = detail.PatientAmount - depositDeduct;
            if (remainingToPay > 0)
            {
                addedPayment = new Payment
                {
                    Invoice = invoice,
                    Amount = remainingToPay,
                    PaymentType = PaymentType.Payment,
                    PaymentMethod = request.Method,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ReceiptEmail = BuildPaymentMeta(null, request.ReferenceCode, request.IdempotencyKey),
                    Note = Truncate(string.IsNullOrWhiteSpace(request.Note) ? "Thu viện phí tại quầy thu ngân" : request.Note.Trim(), 255),
                    PaidAt = now
                };

                _context.Payments.Add(addedPayment);
            }

            await _context.SaveChangesAsync(innerCt);

            string? mainTransCode = addedPayment != null ? $"PT-{addedPayment.Id:D8}" : null;
            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                PatientId = app.PatientId,
                Action = ActionSettleInvoice,
                EntityName = InvoiceEntityName,
                EntityId = invoice.Id,
                IsSuccess = true,
                Severity = "INFO",
                OccurredAt = now,
                Details = JsonSerializer.Serialize(new InvoiceSettlementMeta(depositDeduct, request.IdempotencyKey, mainTransCode))
            });

            await _context.SaveChangesAsync(innerCt);
            return Result.Success(invoice.Id);
        }, ct);
    }

    public async Task<Result<SettleDischargeResultDto>> SettleDischargeAsync(SettleDischargeRequest request, CancellationToken ct = default)
    {
        if (request.AppointmentId <= 0 || !Enum.IsDefined(request.Method))
        {
            return Result.Failure<SettleDischargeResultDto>("INVALID_REQUEST", "Thông tin yêu cầu quyết toán không hợp lệ.");
        }

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var app = await LockAppointmentAsync(request.AppointmentId, innerCt);
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

            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);
            if (shift == null)
            {
                return Result.Failure<SettleDischargeResultDto>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện quyết toán.");
            }

            var insuranceCard = await _context.InsuranceCards
                .FirstOrDefaultAsync(c => c.PatientId == app.PatientId && c.IsActive && c.ValidFrom <= detail.ExamDate && c.ValidTo >= detail.ExamDate, innerCt);

            var activeDeposits = await LockPatientDepositPaymentsAsync(app.PatientId, innerCt);
            var invoice = UpsertPaidInvoice(app, detail, insuranceCard, now);

            decimal patientRemainingOwed = detail.PatientAmount;
            decimal depositDeductedTotal = 0m;
            decimal additionalPaid = 0m;
            decimal refundedToPatient = 0m;
            Payment? primaryPayment = null;

            foreach (var dep in activeDeposits)
            {
                var depMeta = ParseDepositState(dep.ReceiptEmail);
                decimal depBalance = dep.Amount - depMeta.Used - depMeta.Refunded;
                if (depBalance <= 0) continue;

                if (patientRemainingOwed > 0)
                {
                    decimal deduct = Math.Min(patientRemainingOwed, depBalance);
                    dep.ReceiptEmail = BuildDepositState(depMeta.Used + deduct, depMeta.Refunded, depMeta.ReferenceCode);
                    patientRemainingOwed -= deduct;
                    depositDeductedTotal += deduct;
                }
            }

            if (patientRemainingOwed > 0)
            {
                if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
                {
                    return Result.Failure<SettleDischargeResultDto>("MISSING_REFERENCE", "Vui lòng nhập mã chuẩn chi / giao dịch ngân hàng.");
                }

                additionalPaid = patientRemainingOwed;
                primaryPayment = new Payment
                {
                    Invoice = invoice,
                    Amount = additionalPaid,
                    PaymentType = PaymentType.Payment,
                    PaymentMethod = request.Method,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ReceiptEmail = BuildPaymentMeta(null, request.ReferenceCode, request.IdempotencyKey),
                    Note = Truncate(string.IsNullOrWhiteSpace(request.Note)
                        ? "Thu thêm chênh lệch quyết toán viện phí"
                        : request.Note.Trim(), 255),
                    PaidAt = now
                };

                _context.Payments.Add(primaryPayment);
            }
            else
            {
                foreach (var dep in activeDeposits)
                {
                    var depMeta = ParseDepositState(dep.ReceiptEmail);
                    decimal remainingDep = dep.Amount - depMeta.Used - depMeta.Refunded;
                    if (remainingDep <= 0) continue;

                    dep.ReceiptEmail = BuildDepositState(depMeta.Used, depMeta.Refunded + remainingDep, depMeta.ReferenceCode);
                    refundedToPatient += remainingDep;

                    var refundNoteSuffix = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                        ? $" (Người duyệt hoàn: {request.ApprovedBy.Trim()})"
                        : "";
                    var refundReasonText = !string.IsNullOrWhiteSpace(request.Note)
                        ? request.Note.Trim()
                        : "Tất toán xuất viện và hoàn trả tiền mặt số dư tạm ứng";

                    var refPayment = new Payment
                    {
                        Invoice = invoice,
                        Amount = remainingDep,
                        PaymentType = PaymentType.Refund,
                        PaymentMethod = PaymentMethod.Cash,
                        CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                        Note = Truncate($"Hoàn tiền mặt số dư phiếu TU-{dep.Id:D6}: {refundReasonText}{refundNoteSuffix}", 255),
                        PaidAt = now
                    };

                    _context.Payments.Add(refPayment);
                    primaryPayment ??= refPayment;
                }
            }

            await _context.SaveChangesAsync(innerCt);

            string invoiceNum = $"HD-{invoice.Id:D6}";
            string mainTransCode = primaryPayment != null ? $"PT-{primaryPayment.Id:D8}" : invoiceNum;

            _context.AccessLogs.Add(new AccessLog
            {
                AccountId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                PatientId = app.PatientId,
                Action = ActionSettleInvoice,
                EntityName = InvoiceEntityName,
                EntityId = invoice.Id,
                IsSuccess = true,
                Severity = "INFO",
                OccurredAt = now,
                Details = JsonSerializer.Serialize(new InvoiceSettlementMeta(depositDeductedTotal, request.IdempotencyKey, mainTransCode))
            });

            await _context.SaveChangesAsync(innerCt);

            return Result.Success(new SettleDischargeResultDto(
                invoice.Id,
                invoiceNum,
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
                now));
        }, ct);
    }

    public async Task<IReadOnlyList<LedgerItemDto>> GetLedgerAsync(LedgerFilterDto filter, bool exportAll = false, CancellationToken ct = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Invoice)!.ThenInclude(i => i.Appointment)!.ThenInclude(a => a.Patient)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .Include(p => p.Cashier)
            .Where(p => !p.Invoice.Patient.FullName.StartsWith("BN Test"))
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

        var cashierIds = list.Where(p => p.CashierId.HasValue).Select(p => p.CashierId!.Value).Distinct().ToList();
        var shiftRanges = await LoadShiftRangesAsync(cashierIds, ct);

        return list.Select(p =>
        {
            string docNum = p.PaymentType == PaymentType.Deposit
                ? $"TU-{p.Id:D6}"
                : $"HD-{p.InvoiceId:D6}";

            string? refCode = p.PaymentType == PaymentType.Deposit
                ? ParseDepositState(p.ReceiptEmail).ReferenceCode
                : ExtractTag(p.ReceiptEmail, "REF");

            string appCode = p.Invoice?.Appointment?.AppointmentCode ?? "—";
            if (appCode.StartsWith("DEP-", StringComparison.Ordinal))
            {
                appCode = "—";
            }

            return new LedgerItemDto(
                p.Id,
                $"PT-{p.Id:D8}",
                p.PaidAt,
                docNum,
                appCode,
                p.Invoice?.Patient?.FullName ?? p.Invoice?.Appointment?.Patient?.FullName ?? "—",
                p.Amount,
                p.PaymentType,
                p.PaymentMethod,
                refCode,
                p.Cashier?.Username ?? "Hệ thống",
                ResolveShiftId(shiftRanges, p.CashierId, p.PaidAt));
        }).ToList();
    }

    public async Task<IReadOnlyList<PatientBillingHistoryItemDto>> GetPatientBillingHistoryAsync(int patientId, CancellationToken ct = default)
    {
        var invoices = await _context.Invoices
            .AsNoTracking()
            .Include(i => i.Appointment)!.ThenInclude(a => a.Doctor)
            .Include(i => i.Appointment)!.ThenInclude(a => a.Slot)
            .Include(i => i.Payments)
            .Where(i => i.PatientId == patientId
                && (i.TotalAmount > 0 || i.Payments.Any(p => p.PaymentType == PaymentType.Payment)))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        var result = new List<PatientBillingHistoryItemDto>();

        foreach (var inv in invoices)
        {
            var lastPayment = inv.Payments
                .Where(p => p.PaymentType != PaymentType.Deposit)
                .OrderByDescending(p => p.PaidAt)
                .FirstOrDefault();
            var examDate = inv.Appointment?.Slot != null
                ? DateOnly.FromDateTime(inv.Appointment.Slot.StartTime)
                : DateOnly.FromDateTime(inv.CreatedAt);

            result.Add(new PatientBillingHistoryItemDto(
                inv.Id,
                $"HD-{inv.Id:D6}",
                examDate,
                inv.Appointment?.AppointmentCode ?? "—",
                inv.Appointment?.Doctor?.FullName ?? "Bác sĩ",
                inv.TotalAmount,
                inv.InsuranceCoveredAmount,
                inv.PatientAmount,
                inv.Status,
                lastPayment?.PaidAt,
                lastPayment != null ? (lastPayment.PaymentMethod == PaymentMethod.Cash ? "Tiền mặt" : "Chuyển khoản") : null,
                lastPayment != null ? $"PT-{lastPayment.Id:D8}" : null));
        }

        return result;
    }

    public async Task<Result<DepositDto>> CreateDepositAsync(CreateDepositRequest request, CancellationToken ct = default)
    {
        if (request.PatientId <= 0)
            return Result.Failure<DepositDto>("INVALID_PATIENT", "Mã bệnh nhân không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<DepositDto>("INVALID_AMOUNT", "Số tiền tạm ứng phải lớn hơn 0.");

        if (!Enum.IsDefined(request.Method))
            return Result.Failure<DepositDto>("INVALID_METHOD", "Hình thức thanh toán không hợp lệ.");

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

            var (invoice, app) = await EnsureDepositInvoiceAsync(request.PatientId, request.AppointmentId, now, innerCt);
            if (invoice == null)
            {
                return Result.Failure<DepositDto>("NOT_FOUND", "Không tìm thấy hồ sơ khám để gắn phiếu tạm ứng.");
            }

            string approvedByValue = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                ? request.ApprovedBy.Trim()
                : (app?.Doctor != null ? (app.Doctor.FullName.StartsWith("BS") ? app.Doctor.FullName : $"BS. {app.Doctor.FullName}") : "BS. Phạm Thu Hằng");
            string purposeValue = string.IsNullOrWhiteSpace(request.Purpose)
                ? "Tạm ứng viện phí"
                : request.Purpose.Trim();

            var payment = new Payment
            {
                InvoiceId = invoice.Id,
                Amount = request.Amount,
                PaymentType = PaymentType.Deposit,
                PaymentMethod = request.Method,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ReceiptEmail = BuildDepositState(0m, 0m, request.ReferenceCode),
                Note = BuildDepositNote(approvedByValue, purposeValue, request.Note?.Trim()),
                PaidAt = now
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(innerCt);

            if (_context.Database.IsRelational())
            {
                await _context.Payments
                    .Where(p => p.Id == payment.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaymentType, PaymentType.Deposit), innerCt);
                var paymentTypeEntry = _context.Entry(payment).Property(p => p.PaymentType);
                paymentTypeEntry.OriginalValue = PaymentType.Deposit;
                paymentTypeEntry.CurrentValue = PaymentType.Deposit;
                paymentTypeEntry.IsModified = false;
            }

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = request.PatientId,
                    Action = "CREATE_DEPOSIT",
                    EntityName = "payments",
                    EntityId = payment.Id,
                    OccurredAt = now
                });
                await _context.SaveChangesAsync(innerCt);
            }

            var cashierUsername = request.CashierAccountId > 0
                ? await _context.Accounts.AsNoTracking()
                    .Where(a => a.Id == request.CashierAccountId)
                    .Select(a => a.Username)
                    .FirstOrDefaultAsync(innerCt)
                : null;

            payment.Invoice = invoice;
            payment.Invoice.Patient = patient;
            payment.Invoice.Appointment = app!;

            return Result.Success(MapPaymentToDepositDto(payment, cashierUsername));
        }, ct);
    }

    public async Task<PatientDepositSummaryDto> GetPatientDepositSummaryAsync(int patientId, CancellationToken ct = default)
    {
        var deposits = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Cashier)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Appointment)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .Where(p => p.PaymentType == PaymentType.Deposit && p.Invoice.PatientId == patientId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(ct);

        var dtos = deposits.Select(MapPaymentToDepositDto).ToList();
        decimal totalDeposited = dtos.Sum(d => d.Amount);
        decimal totalUsed = dtos.Sum(d => d.UsedAmount);
        decimal totalRefunded = dtos.Sum(d => d.RefundedAmount);
        decimal available = totalDeposited - totalUsed - totalRefunded;

        return new PatientDepositSummaryDto(patientId, totalDeposited, totalUsed, totalRefunded, available, dtos);
    }

    public async Task<IReadOnlyList<DepositDto>> GetDepositsAsync(
        string? search = null,
        PaymentMethod? method = null,
        DepositStatus? status = null,
        int? patientId = null,
        CancellationToken ct = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Appointment)
            .Include(p => p.Cashier)
            .Where(p => p.PaymentType == PaymentType.Deposit);

        if (patientId.HasValue)
            query = query.Where(p => p.Invoice.PatientId == patientId.Value);

        if (method.HasValue)
            query = query.Where(p => p.PaymentMethod == method.Value);

        if (string.IsNullOrWhiteSpace(search) && !patientId.HasValue)
        {
            query = query.Where(p => !p.Invoice.Patient.FullName.StartsWith("BN Test") && !p.Invoice.Patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"));
        }

        var payments = await query
            .OrderByDescending(p => p.PaidAt)
            .Take(500)
            .ToListAsync(ct);

        IEnumerable<DepositDto> dtos = payments.Select(MapPaymentToDepositDto);

        if (status.HasValue)
        {
            dtos = dtos.Where(d => d.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            bool hasIntId = TryParsePatientId(term, out var parsedId);
            dtos = dtos.Where(d =>
                d.DepositCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                || d.PatientName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || d.PhoneNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (d.NationalId != null && d.NationalId.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (hasIntId && d.PatientId == parsedId)
                || (d.ApprovedBy != null && d.ApprovedBy.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (d.AppointmentCode != null && d.AppointmentCode.Contains(term, StringComparison.OrdinalIgnoreCase))
                || (d.ReferenceCode != null && d.ReferenceCode.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        return dtos.Take(150).ToList();
    }

    public async Task<DepositSummaryTotalsDto> GetDepositTotalsAsync(CancellationToken ct = default)
    {
        var rows = await _context.Payments
            .AsNoTracking()
            .Where(p => p.PaymentType == PaymentType.Deposit
                && !p.Invoice.Patient.FullName.StartsWith("BN Test")
                && !p.Invoice.Patient.FullName.StartsWith("Nguyễn Văn Tra Cứu"))
            .Select(p => new { p.Amount, p.ReceiptEmail })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new DepositSummaryTotalsDto(0, 0m, 0m, 0m);
        }

        decimal totalAmount = 0m;
        decimal totalUsed = 0m;
        decimal totalBalance = 0m;

        foreach (var row in rows)
        {
            var meta = ParseDepositState(row.ReceiptEmail);
            totalAmount += row.Amount;
            totalUsed += meta.Used;
            totalBalance += Math.Max(0m, row.Amount - meta.Used - meta.Refunded);
        }

        return new DepositSummaryTotalsDto(rows.Count, totalAmount, totalUsed, totalBalance);
    }

    public async Task<Result<string>> RefundDepositAsync(RefundDepositRequest request, CancellationToken ct = default)
    {
        if (request.DepositId <= 0)
            return Result.Failure<string>("INVALID_DEPOSIT", "Mã phiếu tạm ứng không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<string>("INVALID_AMOUNT", "Số tiền hoàn phải lớn hơn 0.");

        if (!Enum.IsDefined(request.Method))
            return Result.Failure<string>("INVALID_METHOD", "Hình thức thanh toán không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<string>("MISSING_REASON", "Bắt buộc phải nhập lý do hoàn tiền tạm ứng.");

        if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
            return Result.Failure<string>("MISSING_REFERENCE", "Vui lòng nhập mã giao dịch ngân hàng khi hoàn tiền qua chuyển khoản.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            var depositPayment = await LockDepositPaymentByIdAsync(request.DepositId, innerCt);
            if (depositPayment == null || depositPayment.PaymentType != PaymentType.Deposit)
                return Result.Failure<string>("NOT_FOUND", "Không tìm thấy phiếu tạm ứng.");

            var meta = ParseDepositState(depositPayment.ReceiptEmail);
            decimal available = depositPayment.Amount - meta.Used - meta.Refunded;
            if (request.Amount > available)
                return Result.Failure<string>("AMOUNT_EXCEEDED", $"Số tiền yêu cầu hoàn ({request.Amount:N0} đ) vượt quá số dư tạm ứng còn lại ({available:N0} đ).");

            var now = Now;
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);
            if (shift == null)
            {
                return Result.Failure<string>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện hoàn tiền tạm ứng.");
            }

            depositPayment.ReceiptEmail = BuildDepositState(meta.Used, meta.Refunded + request.Amount, meta.ReferenceCode);

            var refundPayment = new Payment
            {
                InvoiceId = depositPayment.InvoiceId,
                Amount = request.Amount,
                PaymentType = PaymentType.Refund,
                PaymentMethod = request.Method,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ReceiptEmail = BuildPaymentMeta(null, request.ReferenceCode, null),
                Note = Truncate($"Hoàn tiền tạm ứng TU-{depositPayment.Id:D6}: {request.Reason.Trim()}", 255),
                PaidAt = now
            };

            _context.Payments.Add(refundPayment);
            await _context.SaveChangesAsync(innerCt);

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = depositPayment.Invoice.PatientId,
                    Action = "REFUND_DEPOSIT",
                    EntityName = "payments",
                    EntityId = refundPayment.Id,
                    OccurredAt = now
                });
                await _context.SaveChangesAsync(innerCt);
            }

            return Result.Success($"PT-{refundPayment.Id:D8}");
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

        var deposits = await _context.Payments
            .AsNoTracking()
            .Include(p => p.Cashier)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Appointment)
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .Where(p => p.PaymentType == PaymentType.Deposit && p.Invoice.PatientId == patient.Id)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(ct);

        var activeSlips = deposits
            .Select(MapPaymentToDepositDto)
            .Where(d => d.Status == DepositStatus.Active && d.Balance > 0)
            .ToList();

        decimal totalDeposited = activeSlips.Sum(s => s.Amount);
        decimal totalUsed = activeSlips.Sum(s => s.UsedAmount);
        decimal totalRefunded = 0m;
        decimal available = activeSlips.Sum(s => s.Balance);
        decimal cashBalance = activeSlips.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Balance);
        decimal bankBalance = activeSlips.Where(s => s.PaymentMethod == PaymentMethod.BankTransfer).Sum(s => s.Balance);

        var latestApp = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Department)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.InvoiceItems)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.LabOrders)!.ThenInclude(o => o.Service)!.ThenInclude(s => s!.ServiceType)
            .Include(a => a.MedicalRecord)!.ThenInclude(m => m!.Prescriptions)!.ThenInclude(p => p.PrescriptionItems)!.ThenInclude(pi => pi.Medication)
            .Where(a => a.PatientId == patient.Id && !a.AppointmentCode.StartsWith("DEP-"))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        IReadOnlyList<InvoiceItemDto> serviceItems = Array.Empty<InvoiceItemDto>();
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
            insuranceCardNum = insuranceCard?.CardNumber;
            baseCoveragePct = GetBaseCoveragePct(insuranceCard);

            var examService = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == _options.ExamFeeServiceId, ct);
            (serviceItems, totalInvoiceAmount, totalInsurancePaid, totalPatientPayable) =
                BuildInvoiceLineItems(latestApp, examService, hasInsurance, baseCoveragePct, useMedicationStrength: true);
        }

        return Result.Success(new QuickPatientDepositLookupDto(
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
            activeSlips));
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

            var activeDeposits = await LockPatientDepositPaymentsAsync(request.PatientId, innerCt);
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
            var addedPayments = new List<Payment>();

            foreach (var deposit in activeDeposits)
            {
                var meta = ParseDepositState(deposit.ReceiptEmail);
                decimal balance = deposit.Amount - meta.Used - meta.Refunded;
                if (balance <= 0) continue;

                deposit.ReceiptEmail = BuildDepositState(meta.Used, meta.Refunded + balance, meta.ReferenceCode);

                var payment = new Payment
                {
                    InvoiceId = deposit.InvoiceId,
                    Amount = balance,
                    PaymentType = PaymentType.Refund,
                    PaymentMethod = deposit.PaymentMethod,
                    CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                    ReceiptEmail = deposit.PaymentMethod == PaymentMethod.BankTransfer
                        ? BuildPaymentMeta(null, request.BankReferenceCode, null)
                        : null,
                    Note = Truncate($"Tất toán hoàn trả số dư phiếu TU-{deposit.Id:D6}: {request.Reason.Trim()}", 255),
                    PaidAt = now
                };

                _context.Payments.Add(payment);
                addedPayments.Add(payment);

                totalRefunded += balance;
                if (deposit.PaymentMethod == PaymentMethod.Cash)
                    cashRefunded += balance;
                else
                    bankRefunded += balance;
            }

            await _context.SaveChangesAsync(innerCt);

            if (request.CashierAccountId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = patient.Id,
                    Action = "BATCH_REFUND_ALL_DEPOSITS",
                    EntityName = "payments",
                    EntityId = addedPayments.FirstOrDefault()?.Id,
                    OccurredAt = now
                });
                await _context.SaveChangesAsync(innerCt);
            }

            var transCodes = addedPayments.Select(p => $"PT-{p.Id:D8}").ToList();

            return Result.Success(new RefundAllDepositsResultDto(
                patient.Id,
                patient.FullName,
                totalRefunded,
                addedPayments.Count,
                cashRefunded,
                bankRefunded,
                transCodes));
        }, ct);
    }

    public async Task<Result<string>> RefundInvoiceAsync(RefundInvoiceRequest request, CancellationToken ct = default)
    {
        if (request.InvoiceId <= 0)
            return Result.Failure<string>("INVALID_INVOICE", "Mã hóa đơn không hợp lệ.");

        if (request.Amount <= 0)
            return Result.Failure<string>("INVALID_AMOUNT", "Số tiền hoàn phải lớn hơn 0.");

        if (!Enum.IsDefined(request.Method))
            return Result.Failure<string>("INVALID_METHOD", "Hình thức thanh toán không hợp lệ.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure<string>("MISSING_REASON", "Bắt buộc phải nhập lý do hoàn tiền viện phí.");

        if (request.Method == PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(request.ReferenceCode))
            return Result.Failure<string>("MISSING_REFERENCE", "Vui lòng nhập mã giao dịch ngân hàng khi hoàn tiền qua chuyển khoản.");

        return await ExecuteInTransactionAsync(async innerCt =>
        {
            Invoice? invoice;
            if (_context.Database.IsNpgsql())
            {
                invoice = await _context.Invoices
                    .FromSqlInterpolated($"SELECT * FROM invoices WHERE id = {request.InvoiceId} FOR UPDATE")
                    .Include(i => i.Payments)
                    .Include(i => i.Appointment)!.ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(innerCt);
            }
            else
            {
                invoice = await _context.Invoices
                    .Include(i => i.Payments)
                    .Include(i => i.Appointment)!.ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, innerCt);
            }

            if (invoice == null)
                return Result.Failure<string>("NOT_FOUND", "Không tìm thấy hóa đơn viện phí.");

            if (invoice.Status == InvoiceStatus.Unpaid)
                return Result.Failure<string>("UNPAID_INVOICE", "Không thể hoàn tiền cho hóa đơn chưa thanh toán.");

            decimal backedByPayments = invoice.Payments.Where(p => p.PaymentType == PaymentType.Payment).Sum(p => p.Amount);
            decimal backedByDeposit = await GetRecordedDepositDeductedAsync(invoice.Id, innerCt);
            decimal totalBacked = backedByPayments + backedByDeposit;
            decimal totalRefunded = invoice.Payments.Where(p => p.PaymentType == PaymentType.Refund).Sum(p => p.Amount);
            decimal maxRefundable = Math.Max(0m, Math.Min(invoice.PatientAmount, totalBacked) - totalRefunded);

            if (request.Amount > maxRefundable)
                return Result.Failure<string>("AMOUNT_EXCEEDED", $"Số tiền hoàn ({request.Amount:N0} đ) vượt quá số tiền đã thu thực tế có thể hoàn (tối đa {maxRefundable:N0} đ).");

            var now = Now;
            var shift = await LockOpenShiftAsync(request.CashierAccountId, innerCt);
            if (shift == null)
            {
                return Result.Failure<string>("SHIFT_REQUIRED", "Thu ngân chưa mở ca làm việc. Vui lòng mở ca trước khi thực hiện hoàn tiền viện phí.");
            }

            var approverSuffix = !string.IsNullOrWhiteSpace(request.ApprovedBy)
                ? $" (Người duyệt: {request.ApprovedBy.Trim()})"
                : "";

            var payment = new Payment
            {
                Invoice = invoice,
                Amount = request.Amount,
                PaymentType = PaymentType.Refund,
                PaymentMethod = request.Method,
                CashierId = request.CashierAccountId > 0 ? request.CashierAccountId : null,
                ReceiptEmail = request.Method == PaymentMethod.BankTransfer
                    ? BuildPaymentMeta(null, request.ReferenceCode, null)
                    : null,
                Note = Truncate($"Hoàn tiền HĐ HD-{invoice.Id:D6}: {request.Reason.Trim()}{approverSuffix}", 255),
                PaidAt = now
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync(innerCt);

            if (request.CashierAccountId > 0 && invoice.PatientId > 0)
            {
                _context.AccessLogs.Add(new AccessLog
                {
                    AccountId = request.CashierAccountId,
                    PatientId = invoice.PatientId,
                    Action = "REFUND_INVOICE",
                    EntityName = "payments",
                    EntityId = payment.Id,
                    OccurredAt = now
                });
                await _context.SaveChangesAsync(innerCt);
            }

            return Result.Success($"PT-{payment.Id:D8}");
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

        var patients = await _context.Patients
            .AsNoTracking()
            .Include(p => p.Appointments)
                .ThenInclude(a => a.Department)
            .Where(p =>
                (hasIntId && p.Id == parsedId)
                || (p.NationalId != null && p.NationalId.Contains(search))
                || p.PhoneNumber.Contains(search)
                || p.FullName.Contains(search))
            .OrderBy(p => p.FullName)
            .Take(15)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Today);

        return patients.Select(p =>
        {
            var latestApp = p.Appointments
                .Where(a => !a.AppointmentCode.StartsWith("DEP-"))
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault()
                ?? p.Appointments.OrderByDescending(a => a.CreatedAt).FirstOrDefault();

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

    private async Task<int?> FindInvoiceIdByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        var log = await _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == InvoiceEntityName && l.Action == ActionSettleInvoice && l.Details != null && l.Details.Contains(idempotencyKey))
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (log?.EntityId != null)
        {
            return log.EntityId.Value;
        }

        var paymentInvoiceId = await _context.Payments
            .AsNoTracking()
            .Where(p => p.ReceiptEmail != null && p.ReceiptEmail.Contains($"IK={idempotencyKey}"))
            .Select(p => (int?)p.InvoiceId)
            .FirstOrDefaultAsync(ct);

        return paymentInvoiceId;
    }

    private Task<Appointment?> LockAppointmentAsync(int appointmentId, CancellationToken ct)
    {
        if (_context.Database.IsNpgsql())
        {
            return _context.Appointments
                .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
                .Include(a => a.Patient)
                .Include(a => a.Slot)
                .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
                .FirstOrDefaultAsync(ct);
        }

        return _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Slot)
            .Include(a => a.Invoice)!.ThenInclude(i => i!.Payments)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
    }

    private Task<Payment?> LockDepositPaymentByIdAsync(int depositPaymentId, CancellationToken ct)
    {
        if (_context.Database.IsNpgsql())
        {
            return _context.Payments
                .FromSqlInterpolated($"SELECT * FROM payments WHERE id = {depositPaymentId} AND payment_type = 'DEPOSIT' FOR UPDATE")
                .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
                .FirstOrDefaultAsync(ct);
        }

        return _context.Payments
            .Include(p => p.Invoice)!.ThenInclude(i => i.Patient)
            .FirstOrDefaultAsync(p => p.Id == depositPaymentId && p.PaymentType == PaymentType.Deposit, ct);
    }

    private async Task<List<Payment>> LockPatientDepositPaymentsAsync(int patientId, CancellationToken ct)
    {
        List<Payment> allDeposits;
        if (_context.Database.IsNpgsql())
        {
            allDeposits = await _context.Payments
                .FromSqlInterpolated($@"
                    SELECT p.*
                    FROM payments p
                    INNER JOIN invoices i ON i.id = p.invoice_id
                    WHERE i.patient_id = {patientId} AND p.payment_type = 'DEPOSIT'
                    ORDER BY p.paid_at
                    FOR UPDATE OF p")
                .ToListAsync(ct);
        }
        else
        {
            allDeposits = await _context.Payments
                .Where(p => p.PaymentType == PaymentType.Deposit && p.Invoice.PatientId == patientId)
                .OrderBy(p => p.PaidAt)
                .ToListAsync(ct);
        }

        return allDeposits
            .Where(p =>
            {
                var meta = ParseDepositState(p.ReceiptEmail);
                return (p.Amount - meta.Used - meta.Refunded) > 0;
            })
            .ToList();
    }

    private async Task<(Invoice? Invoice, Appointment? Appointment)> EnsureDepositInvoiceAsync(
        int patientId,
        int? requestedAppointmentId,
        DateTime now,
        CancellationToken ct)
    {
        Appointment? app = null;

        if (requestedAppointmentId.HasValue)
        {
            app = await _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Invoice)
                .FirstOrDefaultAsync(a => a.Id == requestedAppointmentId.Value, ct);
        }
        else
        {
            app = await _context.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Invoice)
                .Where(a => a.PatientId == patientId && (a.Invoice == null || a.Invoice.Status != InvoiceStatus.Paid))
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (app != null && !app.AppointmentCode.StartsWith("DEP-", StringComparison.Ordinal) && app.StatusId < (int)AppointmentStatusId.CheckedIn)
            {
                app.StatusId = (int)AppointmentStatusId.CheckedIn;
            }
        }

        if (app == null)
        {
            var defaultDoc = await _context.Doctors.AsNoTracking().FirstOrDefaultAsync(ct);
            var defaultDept = await _context.Departments.AsNoTracking().FirstOrDefaultAsync(ct);
            if (defaultDoc == null || defaultDept == null)
            {
                return (null, null);
            }

            app = new Appointment
            {
                PatientId = patientId,
                DoctorId = defaultDoc.Id,
                DepartmentId = defaultDept.Id,
                AppointmentCode = $"DEP-{now:HHmmss}-{Random.Shared.Next(10, 99)}",
                StatusId = (int)AppointmentStatusId.CheckedIn,
                CreatedAt = now
            };
            _context.Appointments.Add(app);
            await _context.SaveChangesAsync(ct);
        }

        if (app.Invoice == null)
        {
            var invoice = new Invoice
            {
                AppointmentId = app.Id,
                PatientId = patientId,
                TotalAmount = 0m,
                InsuranceCoveredAmount = 0m,
                PatientAmount = 0m,
                Status = InvoiceStatus.Unpaid,
                CreatedAt = now
            };
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(ct);
            app.Invoice = invoice;
        }

        return (app.Invoice, app);
    }

    private Invoice UpsertPaidInvoice(Appointment app, InvoiceDetailDto detail, InsuranceCard? insuranceCard, DateTime now)
    {
        var invoice = app.Invoice;
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
                    Category = MapCategory(item.Category),
                    Description = Truncate(item.Description, 255)!,
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

        return invoice;
    }

    private async Task<decimal> GetRecordedDepositDeductedAsync(int invoiceId, CancellationToken ct)
    {
        var log = await _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == InvoiceEntityName && l.EntityId == invoiceId && l.Action == ActionSettleInvoice)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(log?.Details))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<InvoiceSettlementMeta>(log.Details);
                if (meta != null && meta.DepositDeducted > 0)
                {
                    return meta.DepositDeducted;
                }
            }
            catch
            {
                // Ignore malformed audit details
            }
        }

        return 0m;
    }

    private async Task<List<(int CashierId, int ShiftId, DateTime Start, DateTime End)>> LoadShiftRangesAsync(List<int> cashierIds, CancellationToken ct)
    {
        if (cashierIds.Count == 0)
        {
            return new List<(int, int, DateTime, DateTime)>();
        }

        var logs = await _context.AccessLogs
            .AsNoTracking()
            .Where(l => l.EntityName == CashierShiftService.ShiftEntityName
                && l.AccountId.HasValue
                && cashierIds.Contains(l.AccountId.Value))
            .OrderByDescending(l => l.OccurredAt)
            .ToListAsync(ct);

        var ranges = new List<(int CashierId, int ShiftId, DateTime Start, DateTime End)>();
        foreach (var l in logs)
        {
            if (!l.AccountId.HasValue) continue;
            DateTime end = DateTime.MaxValue;
            if (l.Action == CashierShiftService.ActionClosed && !string.IsNullOrWhiteSpace(l.Details))
            {
                try
                {
                    using var doc = JsonDocument.Parse(l.Details);
                    if (doc.RootElement.TryGetProperty("ClosedAt", out var closedEl) && closedEl.ValueKind == JsonValueKind.String && closedEl.TryGetDateTime(out var dt))
                    {
                        end = dt;
                    }
                }
                catch
                {
                    // Use open-ended range on malformed shift payload
                }
            }

            ranges.Add((l.AccountId.Value, checked((int)l.Id), l.OccurredAt, end));
        }

        return ranges;
    }

    private static int? ResolveShiftId(List<(int CashierId, int ShiftId, DateTime Start, DateTime End)> ranges, int? cashierId, DateTime paidAt)
    {
        if (!cashierId.HasValue) return null;
        foreach (var r in ranges)
        {
            if (r.CashierId == cashierId.Value && paidAt >= r.Start && paidAt <= r.End)
            {
                return r.ShiftId;
            }
        }

        return null;
    }

    private decimal GetBaseCoveragePct(InsuranceCard? card)
    {
        if (card == null) return 0m;
        bool isRightRoute = card.IsRegisteredHere || card.InsurancePlan?.ReferralType == ReferralType.InNetwork;
        return isRightRoute ? (card.InsurancePlan?.CoveragePct ?? 80m) : _options.OutOfRoutePct;
    }

    private (IReadOnlyList<InvoiceItemDto> Items, decimal TotalAmount, decimal CoveredAmount, decimal PatientAmount) BuildInvoiceLineItems(
        Appointment app,
        Service? examService,
        bool hasInsurance,
        decimal baseCoveragePct,
        bool useMedicationStrength = false)
    {
        var items = new List<InvoiceItemDto>();
        int itemId = 1;

        decimal examUnitPrice = examService?.Price ?? _options.DefaultExamPrice;
        decimal examLineCoverage = hasInsurance ? baseCoveragePct : 0m;
        decimal examCoveredAmount = Math.Round(examUnitPrice * examLineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
        items.Add(new InvoiceItemDto(
            itemId++,
            "Công khám",
            examService?.Name ?? "Khám chuyên khoa",
            examUnitPrice,
            1,
            examUnitPrice,
            true,
            examLineCoverage,
            examCoveredAmount,
            examUnitPrice - examCoveredAmount));

        if (app.MedicalRecord?.LabOrders != null)
        {
            foreach (var order in app.MedicalRecord.LabOrders)
            {
                if (order.Service == null) continue;
                decimal unitPrice = order.Service.Price;
                decimal lineCoverage = hasInsurance ? baseCoveragePct : 0m;
                decimal coveredAmount = Math.Round(unitPrice * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                items.Add(new InvoiceItemDto(
                    itemId++,
                    order.Service.ServiceType?.Name ?? "Cận lâm sàng",
                    order.Service.Name,
                    unitPrice,
                    1,
                    unitPrice,
                    true,
                    lineCoverage,
                    coveredAmount,
                    unitPrice - coveredAmount));
            }
        }

        if (app.MedicalRecord?.Prescriptions != null)
        {
            foreach (var p in app.MedicalRecord.Prescriptions)
            {
                foreach (var pi in p.PrescriptionItems)
                {
                    if (pi.Medication == null) continue;
                    decimal unitPrice = pi.Medication.Price;
                    int qty = pi.Quantity;
                    decimal total = unitPrice * qty;
                    decimal lineCoverage = hasInsurance ? baseCoveragePct : 0m;
                    decimal coveredAmount = Math.Round(total * lineCoverage / 100m, 0, MidpointRounding.AwayFromZero);
                    string medDesc = useMedicationStrength
                        ? $"{pi.Medication.Name} {(string.IsNullOrWhiteSpace(pi.Medication.Strength) ? "" : $"({pi.Medication.Strength})")}".Trim()
                        : $"{pi.Medication.Name} ({pi.Medication.Unit})";

                    items.Add(new InvoiceItemDto(
                        itemId++,
                        useMedicationStrength ? "Thuốc điều trị" : "Thuốc kê đơn",
                        medDesc,
                        unitPrice,
                        qty,
                        total,
                        true,
                        lineCoverage,
                        coveredAmount,
                        total - coveredAmount));
                }
            }
        }

        return (items, items.Sum(i => i.Total), items.Sum(i => i.CoveredAmount), items.Sum(i => i.PatientAmount));
    }

    private static InvoiceCategory MapCategory(string categoryDisplay)
    {
        if (categoryDisplay.Contains("Thuốc", StringComparison.OrdinalIgnoreCase))
            return InvoiceCategory.Medication;
        if (categoryDisplay.Contains("Khám", StringComparison.OrdinalIgnoreCase))
            return InvoiceCategory.Examination;
        return InvoiceCategory.Service;
    }

    private static DepositDto MapPaymentToDepositDto(Payment p) => MapPaymentToDepositDto(p, null);

    private static DepositDto MapPaymentToDepositDto(Payment p, string? cashierUsernameOverride)
    {
        var meta = ParseDepositState(p.ReceiptEmail);
        var noteMeta = ParseDepositNote(p.Note);
        decimal balance = Math.Max(0m, p.Amount - meta.Used - meta.Refunded);
        var status = ComputeDepositStatus(p.Amount, meta.Used, meta.Refunded);
        var patient = p.Invoice?.Patient;
        int patientId = p.Invoice?.PatientId ?? 0;
        string? appCode = p.Invoice?.Appointment?.AppointmentCode;
        if (appCode != null && appCode.StartsWith("DEP-", StringComparison.Ordinal))
        {
            appCode = null;
        }

        return new DepositDto(
            p.Id,
            $"TU-{p.Id:D6}",
            patientId,
            patientId.ToString("D8"),
            patient?.FullName ?? "Bệnh nhân",
            patient?.PhoneNumber ?? string.Empty,
            p.Invoice?.AppointmentId,
            appCode,
            p.Amount,
            meta.Used,
            meta.Refunded,
            balance,
            status,
            p.PaymentMethod,
            meta.ReferenceCode,
            noteMeta.Purpose,
            noteMeta.UserNote,
            cashierUsernameOverride ?? p.Cashier?.Username ?? "Hệ thống",
            p.PaidAt,
            patient?.NationalId,
            noteMeta.ApprovedBy);
    }

    private static string BuildDepositState(decimal used, decimal refunded, string? referenceCode)
    {
        string u = used.ToString("0.##", CultureInfo.InvariantCulture);
        string r = refunded.ToString("0.##", CultureInfo.InvariantCulture);
        string cleanRef = SanitizeTagValue(referenceCode, 30);
        string result = string.IsNullOrEmpty(cleanRef)
            ? $"#U={u};R={r}#"
            : $"#U={u};R={r};REF={cleanRef}#";
        return Truncate(result, 100)!;
    }

    private static DepositMeta ParseDepositState(string? receiptEmail)
    {
        if (string.IsNullOrWhiteSpace(receiptEmail) || !receiptEmail.StartsWith("#U=", StringComparison.Ordinal))
        {
            return new DepositMeta(0m, 0m, null);
        }

        decimal used = decimal.TryParse(ExtractTag(receiptEmail, "U"), NumberStyles.Number, CultureInfo.InvariantCulture, out var uVal) ? uVal : 0m;
        decimal refunded = decimal.TryParse(ExtractTag(receiptEmail, "R"), NumberStyles.Number, CultureInfo.InvariantCulture, out var rVal) ? rVal : 0m;
        string? refCode = ExtractTag(receiptEmail, "REF");
        return new DepositMeta(used, refunded, string.IsNullOrEmpty(refCode) ? null : refCode);
    }

    private static string BuildDepositNote(string approvedBy, string purpose, string? userNote)
    {
        string cleanBy = SanitizeTagValue(approvedBy, 60);
        string cleanPurpose = SanitizeTagValue(purpose, 90);
        string prefix = $"#BY={cleanBy};PUR={cleanPurpose}#";
        if (string.IsNullOrWhiteSpace(userNote))
        {
            return Truncate(prefix, 255)!;
        }

        return Truncate($"{prefix}{userNote.Trim()}", 255)!;
    }

    private static DepositNoteMeta ParseDepositNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return new DepositNoteMeta("BS. Phạm Thu Hằng", "Tạm ứng viện phí", null);
        }

        if (!note.StartsWith("#BY=", StringComparison.Ordinal))
        {
            return new DepositNoteMeta("BS. Phạm Thu Hằng", "Tạm ứng viện phí", note);
        }

        int closeIdx = note.IndexOf('#', 1);
        string header = closeIdx > 0 ? note[..closeIdx] : note;
        string? userNote = closeIdx > 0 && closeIdx + 1 < note.Length ? note[(closeIdx + 1)..] : null;
        string by = ExtractTag(header, "BY") ?? "BS. Phạm Thu Hằng";
        string pur = ExtractTag(header, "PUR") ?? "Tạm ứng viện phí";
        return new DepositNoteMeta(by, pur, string.IsNullOrWhiteSpace(userNote) ? null : userNote);
    }

    private static DepositStatus ComputeDepositStatus(decimal amount, decimal used, decimal refunded)
    {
        if (amount - used - refunded <= 0)
        {
            return refunded > 0 ? DepositStatus.Refunded : DepositStatus.Depleted;
        }

        return DepositStatus.Active;
    }

    private static string? BuildPaymentMeta(string? email, string? referenceCode, string? idempotencyKey)
    {
        string cleanRef = SanitizeTagValue(referenceCode, 30);
        string cleanIk = SanitizeTagValue(idempotencyKey, 40);
        string cleanEm = SanitizeTagValue(email, 36);

        if (string.IsNullOrEmpty(cleanRef) && string.IsNullOrEmpty(cleanIk))
        {
            return Truncate(email?.Trim(), 100);
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(cleanEm)) parts.Add($"EM={cleanEm}");
        if (!string.IsNullOrEmpty(cleanRef)) parts.Add($"REF={cleanRef}");
        if (!string.IsNullOrEmpty(cleanIk)) parts.Add($"IK={cleanIk}");
        return Truncate($"#{string.Join(';', parts)}#", 100);
    }

    private static string? ExtractTag(string? field, string key)
    {
        if (string.IsNullOrWhiteSpace(field) || !field.StartsWith('#'))
        {
            return null;
        }

        string body = field.Trim('#');
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Equals(key, StringComparison.Ordinal))
            {
                return part[(eq + 1)..];
            }
        }

        return null;
    }

    private static string SanitizeTagValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Trim().Replace("#", " ").Replace(";", ",");
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
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
}
