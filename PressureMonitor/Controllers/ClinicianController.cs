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
    
    [HttpGet]
    public async Task<IActionResult> ViewPatientPressureMap(int patientId, string day)
    {
        // Get the logged in clinician's user ID
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        // Check that the clinician exists and get their record
        var clinician = await context.Clinicians.FirstOrDefaultAsync(c => c.UserId == userId);
        if (clinician == null)
        {
            return RedirectToAction("Login", "Account");
        }

        // Check that this patient is assigned to this clinician
        var patient = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            .FirstOrDefaultAsync(p => p.Id == patientId && p.ClinicianId == clinician.Id);

        if (patient == null)
        {
            TempData["Error"] = "Patient not found or not assigned to you.";
            return RedirectToAction("Index");
        }

        // This should never be an issue but just in case
        if (string.IsNullOrWhiteSpace(day) || !DateOnly.TryParse(day, out var dateOnly))
        {
            TempData["Error"] = "Invalid date format.";
            return RedirectToAction("Index");
        }

        // Check if the patient has any pressure maps for this day (which they should)
        var hasDataForDay = patient.PressureMaps.Any(pm => pm.Day == dateOnly);
        if (!hasDataForDay)
        {
            TempData["Error"] = "No pressure map data found for this date.";
            return RedirectToAction("Index");
        }
        ViewBag.Clinician = clinician;
        ViewBag.Patient = patient;
        ViewBag.Day = dateOnly;
        ViewBag.PatientId = patientId;
        ViewBag.ClinicianId = clinician.Id;
        return View();
    }
}

