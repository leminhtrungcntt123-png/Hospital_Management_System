using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Areas.Patient.Controllers;

[Area("Patient")]
[AllowAnonymous]
public class BookingController : Controller
{
    public IActionResult Index(int? doctorId) => RedirectToAction("Index", "Booking", new { area = "", doctorId });
}
