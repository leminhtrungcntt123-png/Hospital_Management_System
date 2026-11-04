namespace HIS.Domain;

/// <summary>
/// Danh mục vai trò trong hệ thống.
/// Giá trị khớp dữ liệu thật của bảng roles trong CSDL — không đổi tuỳ ý vì
/// HIS.BLL/Security/RolePermissions.cs phân nhánh theo mã vai trò.
/// </summary>
public enum RoleId
{
    Admin = 1,
    Doctor = 2,
    Nurse = 3,
    Receptionist = 4,
    Cashier = 5,
    DepartmentHead = 6,
    Patient = 7
}

/// <summary>
/// Danh mục phân loại dịch vụ (khớp seed service_types trong CSDL).
/// </summary>
public enum ServiceTypeId
{
    Examination = 1,
    LabTest = 2,
    Imaging = 3,
    Functional = 4
}

/// <summary>
/// Trạng thái lịch hẹn khám. Id là cố định vì hai index một phần trong CSDL
/// lọc theo id (idx_appointments_hold WHERE status_id = 1 và
/// uq_patient_slot_active WHERE status_id IN (1,2,3,4,5)).
/// </summary>
public enum AppointmentStatusId
{
    Pending = 1,
    Confirmed = 2,
    CheckedIn = 3,
    ReadyForDoctor = 4,
    InProgress = 5,
    Completed = 6,
    Cancelled = 7,
    NoShow = 8,
    Expired = 9,
    RescheduleRequired = 10
}

/// <summary>Giới tính của người khám (patients.gender).</summary>
public enum Gender
{
    Male,
    Female,
    Other
}

/// <summary>Ca khám. Ca tối là ca trực (duty_shifts), không sinh slot khám.</summary>
public enum ExamShift
{
    Morning,
    Afternoon
}

/// <summary>Trạng thái một khung khám (appointment_slots.status).</summary>
public enum SlotStatus
{
    Available,
    Full,
    Blocked
}

/// <summary>Kênh phát sinh lịch hẹn (appointments.booking_channel).</summary>
public enum BookingChannel
{
    Web,
    OnSite
}

/// <summary>Loại lượt khám (appointments.visit_type).</summary>
public enum VisitType
{
    Scheduled,
    WalkIn
}

/// <summary>Lý do ngoại lệ lịch làm việc của bác sĩ (schedule_exceptions.type).</summary>
public enum ScheduleExceptionType
{
    Leave,
    Training,
    Surgery,
    ShiftSwap
}

/// <summary>Trạng thái duyệt ngoại lệ lịch (schedule_exceptions.status).</summary>
public enum ScheduleExceptionStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>Quan hệ giữa chủ tài khoản và hồ sơ người khám (account_patient_profiles.relationship).</summary>
public enum PatientRelationship
{
    Self,
    Spouse,
    Child,
    Parent,
    Sibling,
    Other
}

/// <summary>Trạng thái liên kết tài khoản — hồ sơ (account_patient_profiles.status).</summary>
public enum LinkStatus
{
    Active,
    Pending,
    Revoked
}

/// <summary>Phân loại chi phí trên dòng hoá đơn (invoice_items.category).</summary>
public enum InvoiceCategory
{
    Examination,
    Medication,
    Service
}

/// <summary>Trạng thái hoá đơn (invoices.status).</summary>
public enum InvoiceStatus
{
    Unpaid,
    PartiallyPaid,
    Paid
}

/// <summary>Loại thanh toán (payments.payment_type).</summary>
public enum PaymentType
{
    Deposit,
    Payment,
    Refund
}

/// <summary>Hình thức thanh toán (payments.payment_method).</summary>
public enum PaymentMethod
{
    Cash,
    BankTransfer
}

/// <summary>Trạng thái chỉ định cận lâm sàng (lab_orders.status).</summary>
public enum LabOrderStatus
{
    Pending,
    Completed
}

/// <summary>Tuyến khám bảo hiểm (insurance_plans.referral_type).</summary>
public enum ReferralType
{
    InNetwork,
    OutOfNetwork
}
