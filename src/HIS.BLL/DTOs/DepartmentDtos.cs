namespace HIS.BLL.DTOs;

public record DepartmentListItemDto(int Id, string Name, string Description);

public record ClinicRoomDto(int Id, int DepartmentId, string RoomName, string RoomNumber);

