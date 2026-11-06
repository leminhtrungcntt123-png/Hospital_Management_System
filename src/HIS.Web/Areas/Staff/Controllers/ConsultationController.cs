using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
[Authorize(Policy = "DoctorOnly")]
public class ConsultationController : Controller
{
    // Màn hình bác sĩ khám bệnh & kê đơn (Doctor)
    [HttpGet]
    public IActionResult Index() => View();
}
