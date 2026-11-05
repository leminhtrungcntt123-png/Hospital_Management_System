using HIS.BLL.DTOs;

namespace HIS.Web.Models;

public sealed class ReceptionViewModel
{
    public ReceptionDesk Desk { get; set; } = null!;
    public string? Search { get; set; }
    public WalkInReceptionRequest WalkIn { get; set; } = new();
}
