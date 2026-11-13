namespace HIS.Domain;

public enum Gender
{
    Male,
    Female,
    Other
}

public enum ReferralType
{
    InNetwork,
    OutOfNetwork
}

public enum ExamShift
{
    Morning,
    Afternoon
}

public enum EmploymentType
{
    FullTime,
    PartTime
}

public enum SchedulingPeriodStatus
{
    Draft,
    Open,
    Reviewing,
    Published,
    Closed
}

public enum DutyShiftStatus
{
    Bidding,
    Approved,
    Rejected,
    Cancelled
}

public enum DoctorScheduleStatus
{
    Bidding,
    Approved,
    Rejected,
    Reallocated,
    Cancelled
}

public enum AuditSeverity
{
    Info,
    Warning,
    Security,
    Critical
}

public enum ScheduleExceptionType
{
    Leave,
    Training,
    Surgery,
    ShiftSwap
}

public enum ScheduleExceptionStatus
{
    Pending,
    Approved,
    Rejected
}

public enum SlotStatus
{
    Available,
    Full,
    Blocked
}

public enum BookingChannel
{
    Web,
    OnSite
}

public enum VisitType
{
    Scheduled,
    WalkIn
}

public enum LabOrderStatus
{
    Pending,
    Completed
}

public enum InvoiceStatus
{
    Unpaid,
    PartiallyPaid,
    Paid
}

public enum PaymentType
{
    Deposit,
    Payment,
    Refund
}

public enum PaymentMethod
{
    Cash,
    BankTransfer
}

public enum InvoiceCategory
{
    Examination,
    Medication,
    Service
}

public enum PatientRelationship
{
    Self,
    Spouse,
    Child,
    Parent,
    Sibling,
    Other
}

public enum LinkStatus
{
    Active,
    Pending,
    Revoked
}

public enum RoleId
{
    Admin = 1,
    DepartmentHead = 2,
    Doctor = 3,
    Nurse = 4,
    Patient = 5
}

public enum ServiceTypeId
{
    Examination = 1,
    LabTest = 2,
    Imaging = 3
}

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

