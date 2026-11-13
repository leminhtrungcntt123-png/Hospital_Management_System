using HIS.Domain;

namespace HIS.BLL.DTOs;

public record InvoiceItemDto(
    int Id,
    string Category,
    string Description,
    decimal UnitPrice,
    int Quantity,
    decimal Total,
    bool IsInsuranceCovered,
    decimal CoveragePctApplied,
    decimal CoveredAmount,
    decimal PatientAmount);

public record InvoiceDetailDto(
    int InvoiceId,
    string InvoiceNumber,
    int AppointmentId,
    string AppointmentCode,
    DateOnly ExamDate,
    int PatientId,
    string PatientCode,
    string PatientName,
    string PatientPhone,
    DateOnly DateOfBirth,
    int Age,
    string Gender,
    string DoctorName,
    string DepartmentName,
    DateTime AppointmentTime,
    bool HasInsurance,
    string? InsuranceCardNumber,
    decimal CoveragePct,
    string? RegisteredFacility,
    IReadOnlyList<InvoiceItemDto> Items,
    decimal TotalAmount,
    decimal InsuranceCoveredAmount,
    decimal PatientAmount,
    InvoiceStatus Status,
    DateTime? PaidAt,
    string? PaymentMethod,
    string? TransactionCode,
    string? CashierName,
    decimal AvailableDepositBalance = 0m,
    IReadOnlyList<DepositDto>? PatientDeposits = null,
    decimal DepositDeducted = 0m,
    decimal AdditionalPaid = 0m,
    decimal RefundedAmount = 0m,
    string? NationalId = null,
    string? Address = null,
    string? Diagnosis = null);

public record PendingInvoiceItemDto(
    int AppointmentId,
    string AppointmentCode,
    string PatientCode,
    string PatientName,
    string PhoneNumber,
    string DepartmentName,
    string DoctorName,
    DateOnly ExamDate,
    string Status,
    decimal TotalAmount,
    bool HasInsurance,
    InvoiceStatus InvoiceStatus,
    string? InvoiceNumber,
    string? NationalId = null,
    decimal DepositBalance = 0m,
    decimal PatientPayAmount = 0m);

public record CheckoutInvoiceRequest(
    int AppointmentId,
    PaymentMethod Method,
    string? ReferenceCode,
    string? Note,
    int CashierAccountId,
    string? IdempotencyKey = null,
    int? DepositId = null,
    decimal? DepositDeductAmount = null);

public record LedgerFilterDto(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? CashierId = null,
    PaymentMethod? Method = null,
    PaymentType? Type = null);

public record LedgerItemDto(
    int PaymentId,
    string TransactionCode,
    DateTime PaidAt,
    string InvoiceNumber,
    string AppointmentCode,
    string PatientName,
    decimal Amount,
    PaymentType PaymentType,
    PaymentMethod PaymentMethod,
    string? ReferenceCode,
    string CashierName,
    int? ShiftId);

public record CashierShiftDto(
    int Id,
    int CashierId,
    string CashierName,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal OpeningCash,
    decimal? ExpectedCash,
    decimal? CountedCash,
    decimal? Difference,
    ShiftStatus Status,
    string? Note);

public record OpenShiftRequest(
    int CashierAccountId,
    decimal OpeningCash,
    string? Note);

public record CloseShiftRequest(
    int ShiftId,
    int CashierAccountId,
    decimal CountedCash,
    string? Note);

public record PatientBillingHistoryItemDto(
    int InvoiceId,
    string InvoiceNumber,
    DateOnly ExamDate,
    string AppointmentCode,
    string DoctorName,
    decimal TotalAmount,
    decimal InsuranceCoveredAmount,
    decimal PatientAmount,
    InvoiceStatus Status,
    DateTime? PaidAt,
    string? PaymentMethod,
    string? TransactionCode);

public record VietQrResponseDto(
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountHolder,
    decimal Amount,
    string Message,
    string Payload,
    string QrDataUri);

public record DepositDto(
    int Id,
    string DepositCode,
    int PatientId,
    string PatientCode,
    string PatientName,
    string PhoneNumber,
    int? AppointmentId,
    string? AppointmentCode,
    decimal Amount,
    decimal UsedAmount,
    decimal RefundedAmount,
    decimal Balance,
    DepositStatus Status,
    PaymentMethod PaymentMethod,
    string? ReferenceCode,
    string? Purpose,
    string? Note,
    string CashierName,
    DateTime CreatedAt,
    string? NationalId = null,
    string? ApprovedBy = null);

public record CreateDepositRequest(
    int PatientId,
    int? AppointmentId,
    decimal Amount,
    PaymentMethod Method,
    string? Purpose,
    string? ReferenceCode,
    string? Note,
    int CashierAccountId,
    string? ApprovedBy = null);

public record DeductDepositRequest(
    int AppointmentId,
    int DepositId,
    decimal Amount,
    int CashierAccountId,
    string? Note);

public record RefundDepositRequest(
    int DepositId,
    decimal Amount,
    string Reason,
    PaymentMethod Method,
    string? ReferenceCode,
    int CashierAccountId);

public record RefundInvoiceRequest(
    int InvoiceId,
    decimal Amount,
    string Reason,
    PaymentMethod Method,
    string? ReferenceCode,
    int CashierAccountId,
    string? ApprovedBy = null);

public record PatientDepositSummaryDto(
    int PatientId,
    decimal TotalDeposited,
    decimal TotalUsed,
    decimal TotalRefunded,
    decimal AvailableBalance,
    IReadOnlyList<DepositDto> ActiveDeposits);

public record RefundAllPatientDepositsRequest(
    int PatientId,
    string Reason,
    int CashierAccountId,
    string? BankReferenceCode = null);

public record RefundAllDepositsResultDto(
    int PatientId,
    string PatientName,
    decimal TotalRefunded,
    int SlipsRefundedCount,
    decimal CashRefunded,
    decimal BankTransferRefunded,
    IReadOnlyList<string> TransactionCodes);

public record QuickPatientDepositLookupDto(
    int PatientId,
    string PatientCode,
    string FullName,
    string? NationalId,
    string PhoneNumber,
    decimal TotalDeposited,
    decimal TotalUsed,
    decimal TotalRefunded,
    decimal AvailableBalance,
    int ActiveSlipsCount,
    decimal CashBalance,
    decimal BankTransferBalance,
    IReadOnlyList<DepositDto> ActiveSlips,
    string? InsuranceCardNumber = null,
    decimal InsuranceCoveragePct = 0m,
    string? CurrentAppointmentCode = null,
    string? CurrentDepartmentName = null,
    string? CurrentDoctorName = null,
    DateTime? AdmissionDate = null,
    decimal TotalInvoiceAmount = 0m,
    decimal TotalInsurancePaid = 0m,
    decimal TotalPatientPayable = 0m,
    IReadOnlyList<InvoiceItemDto>? ServiceItems = null,
    IReadOnlyList<DepositDto>? AllSlips = null);

public record PatientSearchResultDto(
    int Id,
    string PatientCode,
    string FullName,
    string? NationalId,
    string PhoneNumber,
    DateOnly DateOfBirth,
    int Age,
    string Gender,
    string? Address,
    int? ActiveAppointmentId,
    string? ActiveAppointmentCode,
    string? ActiveDepartmentName);

public record SettleDischargeRequest(
    int AppointmentId,
    PaymentMethod Method,
    string? ReferenceCode,
    string? Note,
    int CashierAccountId,
    string? IdempotencyKey = null,
    string? ApprovedBy = null);

public record SettleDischargeResultDto(
    int InvoiceId,
    string InvoiceNumber,
    string AppointmentCode,
    string PatientName,
    decimal TotalInvoiceAmount,
    decimal InsuranceCoveredAmount,
    decimal PatientPayable,
    decimal DepositDeducted,
    decimal AdditionalPaid,
    decimal RefundedToPatient,
    PaymentMethod Method,
    string TransactionCode,
    DateTime SettledAt);

public record DepositSummaryTotalsDto(
    int TotalCount,
    decimal TotalAmount,
    decimal TotalUsed,
    decimal TotalBalance);
