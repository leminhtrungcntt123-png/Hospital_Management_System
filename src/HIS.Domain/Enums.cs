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

public enum PaymentStatus
{
    Pending,
    Confirmed
}

public enum ShiftStatus
{
    Open,
    Closed
}

public enum DepositStatus
{
    Active,
    Depleted,
    Refunded
}

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    DepositDeduction
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
    Patient = 5,
    Accountant = 6
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

