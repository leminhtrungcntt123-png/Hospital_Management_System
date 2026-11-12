using Microsoft.AspNetCore.Mvc;
using HIS.BLL.Services;

namespace HIS.Web.Controllers;

public class HomeController(
    IDoctorService doctors,
    IDepartmentService departments,
    ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        if (User.IsInRole("DEPARTMENT_HEAD"))
        {
            return RedirectToAction("Index", "Schedule", new { area = "Staff" });
        }

        if (User.IsInRole("DOCTOR"))
        {
            return RedirectToAction("Index", "Consultation", new { area = "Staff" });
        }

        if (User.IsInRole("NURSE"))
        {
            return RedirectToAction("Index", "Reception", new { area = "Staff" });
        }

        if (User.IsInRole("ADMIN"))
        {
            return RedirectToAction("Index", "Management", new { area = "Admin" });
        }

        var departmentList = await departments.GetDepartmentsAsync(ct);
        var featuredDoctors = await doctors.GetPublicFeaturedDoctorsAsync(8, ct);
        ViewBag.FeaturedDoctors = featuredDoctors;
        return View(departmentList);
    }

    [HttpGet, HttpPost]
    [ActionName("NotFound")]
    [Route("404")]
    [Route("NotFound")]
    [Route("Home/NotFound")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult NotFoundPage(int? statusCode = null)
    {
        Response.StatusCode = statusCode ?? StatusCodes.Status404NotFound;
        return View("NotFound");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        try
        {
            var doctorList = await doctors.GetDoctorsAsync(ct);
            var departmentList = await departments.GetDepartmentsAsync(ct);
            return Json(new
            {
                doctors = doctorList.Select(doctor => new
                {
                    doctor.Id,
                    name = doctor.FullName,
                    doctor.Specialty,
                    department = doctor.DepartmentName
                }),
                departments = departmentList.Select(department => new
                {
                    department.Id,
                    department.Name,
                    department.Description
                })
            });
        }
        catch (OperationCanceledException)
        {
            // Trình duyệt reload/chuyển trang làm hủy request - tránh Visual Studio debug exception popup
            return new EmptyResult();
        }
        catch (Exception error)
        {
            logger.LogError(error, "Public catalog could not be loaded: {Message}", error.Message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Chưa tải được thông tin bác sĩ và chuyên khoa. Vui lòng thử lại sau." });
        }
    }
}
