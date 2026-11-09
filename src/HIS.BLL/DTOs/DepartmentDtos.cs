namespace HIS.BLL.DTOs;

public record DepartmentListItemDto(
    int Id,
    string Name,
    string Description);

public record ClinicRoomDto(
    int Id,
    int DepartmentId,
    string RoomName,
    string RoomNumber);

public enum DepartmentStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}
