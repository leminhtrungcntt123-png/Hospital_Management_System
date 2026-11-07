using HIS.Domain;

namespace HIS.BLL.DTOs;

public record InvoiceItemDto(
    int Id,
    string Category,
    string Description,
    decimal UnitPrice,
    int Quantity,
    decimal Total);

public record InvoiceDetailDto(
    int InvoiceId,
    int AppointmentId,
    string AppointmentCode,
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
    string? PaymentMethod)
{
    public string InvoiceNumber => InvoiceId > 0 ? $"HD-{InvoiceId:D6}" : "Chưa lập";
}

public record PendingInvoiceItemDto(
    int AppointmentId,
    string AppointmentCode,
    string PatientCode,
    string PatientName,
    string PhoneNumber,
    string DepartmentName,
    string DoctorName,
    DateTime AppointmentTime,
    string Status,
    decimal TotalAmount,
    bool HasInsurance,
    InvoiceStatus InvoiceStatus);

public record CheckoutInvoiceRequest(
    int AppointmentId,
    PaymentMethod Method,
    string? Note,
    int CashierAccountId);
