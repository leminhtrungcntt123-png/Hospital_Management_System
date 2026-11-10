using HIS.BLL.DTOs;

namespace HIS.Web.ViewModels
{
    public sealed class ReceptionViewModel
    {
        public ReceptionDesk Desk { get; set; } = null!;
        public string? Search { get; set; }
        public string ActiveTab { get; set; } = "web-visits";
        public WalkInReceptionRequest WalkIn { get; set; } = new();
        public WalkInTicketDto? Ticket { get; set; }
    }
}

namespace HIS.Web.Models
{
    public sealed class ReceptionViewModel
    {
        public ReceptionDesk Desk { get; set; } = null!;
        public string? Search { get; set; }
        public string ActiveTab { get; set; } = "web-visits";
        public WalkInReceptionRequest WalkIn { get; set; } = new();
        public WalkInTicketDto? Ticket { get; set; }
    }
}
