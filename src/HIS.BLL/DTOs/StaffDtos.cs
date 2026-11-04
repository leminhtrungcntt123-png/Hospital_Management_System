namespace HIS.BLL.DTOs;

public record StaffListItemDto(int Id, string Username, string FullName, string RoleName, string RoleCode, bool IsActive);

public record RoleOptionDto(int Id, string Code, string Name);

