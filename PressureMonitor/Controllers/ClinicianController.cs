using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Clinician")]
public class ClinicianController(ILogger<ClinicianController> logger, ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var clinician = await context.Clinicians
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (clinician == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(clinician);
    }
}

