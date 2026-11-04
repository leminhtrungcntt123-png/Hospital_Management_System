using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class CatalogController : Controller
{
    // Màn hình quản lý chuyên khoa và phòng khám (Department & Clinic Rooms)
    [HttpGet]
    public IActionResult Index() => View();
}
