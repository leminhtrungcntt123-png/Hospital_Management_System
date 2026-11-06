using Microsoft.AspNetCore.Mvc;
using HIS.BLL.Services;

namespace HIS.Web.Controllers;

public class HomeController(
    IDoctorService doctors,
    IDepartmentService departments,
    ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError("Public catalog could not be loaded: {Type}", error.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Chưa tải được thông tin bác sĩ và chuyên khoa. Vui lòng thử lại sau." });
        }
    }
}
