using HIS.Domain;

namespace HIS.BLL.Common;

public static class AppointmentStateMachine
{
    private static readonly Dictionary<AppointmentStatusId, HashSet<AppointmentStatusId>> AllowedTransitions = new()
    {
        [AppointmentStatusId.Pending] = [AppointmentStatusId.Confirmed, AppointmentStatusId.Expired, AppointmentStatusId.Cancelled, AppointmentStatusId.RescheduleRequired],
        [AppointmentStatusId.Confirmed] = [AppointmentStatusId.CheckedIn, AppointmentStatusId.Cancelled, AppointmentStatusId.NoShow, AppointmentStatusId.RescheduleRequired],
        [AppointmentStatusId.CheckedIn] = [AppointmentStatusId.ReadyForDoctor, AppointmentStatusId.Cancelled],
        [AppointmentStatusId.ReadyForDoctor] = [AppointmentStatusId.InProgress, AppointmentStatusId.Completed],
        [AppointmentStatusId.InProgress] = [AppointmentStatusId.Completed],
        [AppointmentStatusId.RescheduleRequired] = [AppointmentStatusId.Cancelled, AppointmentStatusId.Pending, AppointmentStatusId.Confirmed],
        [AppointmentStatusId.Completed] = [],
        [AppointmentStatusId.Cancelled] = [],
        [AppointmentStatusId.NoShow] = [],
        [AppointmentStatusId.Expired] = []
    };

    public static bool CanTransition(AppointmentStatusId current, AppointmentStatusId next)
    {
        return AllowedTransitions.TryGetValue(current, out var targets) && targets.Contains(next);
    }

    public static bool IsTerminal(AppointmentStatusId status)
    {
        return status is AppointmentStatusId.Completed
            or AppointmentStatusId.Cancelled
            or AppointmentStatusId.NoShow
            or AppointmentStatusId.Expired;
    }
}

