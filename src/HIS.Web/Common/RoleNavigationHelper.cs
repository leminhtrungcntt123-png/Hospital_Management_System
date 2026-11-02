namespace HIS.Web.Common;

public static class RoleNavigationHelper
{
    public static (string Action, string Controller, string Area) GetLandingRoute(string? role) =>
        role switch
        {
            "ADMIN" => ("Index", "Accounts", "Admin"),
            "DEPARTMENT_HEAD" => ("Index", "Schedule", "Staff"),
            "DOCTOR" => ("Index", "Consultation", "Staff"),
            "NURSE" => ("Index", "Reception", "Staff"),
            "PATIENT" => ("Index", "Booking", "Patient"),
            _ => ("Index", "Home", "")
        };
}
