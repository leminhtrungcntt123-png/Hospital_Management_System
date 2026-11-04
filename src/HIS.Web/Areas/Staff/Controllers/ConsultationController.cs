using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Staff.Controllers;

[Area("Staff")]
public class ConsultationController : Controller
{
    // Màn hình bác sĩ khám bệnh & kê đơn (Doctor)
    [HttpGet]
    public IActionResult Index() => View();
}
