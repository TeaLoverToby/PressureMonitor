using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Patient")]
public class PatientController(ILogger<PatientController> logger, ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var patient = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(patient);
    }

    [HttpGet]
    public async Task<IActionResult> Upload()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var patient = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(patient);
    }

    [HttpGet]
    public async Task<IActionResult> ViewPressureMap(string day)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var patient = await context.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!DateOnly.TryParse(day, out var dayOnly))
        {
            TempData["Error"] = "Invalid day format.";
            return RedirectToAction(nameof(Upload));
        }

        var pressureMap = await context.PressureMaps
            .Include(pm => pm.Frames)
            .FirstOrDefaultAsync(pm => pm.PatientId == patient.Id && pm.Day == dayOnly);

        if (pressureMap == null)
        {
            TempData["Error"] = "No data found for that day.";
            return RedirectToAction(nameof(Upload));
        }

        return View(pressureMap);
    }
}

