using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace HIS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public class CatalogController : Controller
{
    // Màn hình quản lý chuyên khoa và phòng khám (Department & Clinic Rooms)
    [HttpGet]
    public IActionResult Index() => View();
}
