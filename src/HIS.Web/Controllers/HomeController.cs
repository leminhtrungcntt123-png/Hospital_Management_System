using HIS.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HIS.Web.Controllers;

public class HomeController : Controller
{
    private readonly IDoctorService _doctorService;
    private readonly IDepartmentService _departmentService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IDoctorService doctorService,
        IDepartmentService departmentService,
        ILogger<HomeController> logger)
    {
        _doctorService = doctorService;
        _departmentService = departmentService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (User.IsInRole("DEPARTMENT_HEAD") || User.IsInRole("DEPT_HEAD"))
        {
            return RedirectToAction("Index", "Schedule", new { area = "Staff" });
        }

        if (User.IsInRole("DOCTOR"))
        {
            return RedirectToAction("Index", "Consultation", new { area = "Staff" });
        }

        if (User.IsInRole("NURSE") || User.IsInRole("RECEPTIONIST"))
        {
            return RedirectToAction("Index", "Reception", new { area = "Staff" });
        }

        if (User.IsInRole("ADMIN"))
        {
            return RedirectToAction("Index", "Management", new { area = "Admin" });
        }

        return RedirectToAction("Index", "Booking");
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
    public async Task<IActionResult> Catalog(CancellationToken ct = default)
    {
        try
        {
            var doctorList = await _doctorService.GetDoctorsAsync(ct);
            var departmentList = await _departmentService.GetDepartmentsAsync(ct);
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogError("Public catalog could not be loaded: {Type}", error.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Chưa tải được thông tin bác sĩ và chuyên khoa. Vui lòng thử lại sau." });
        }
    }
}

