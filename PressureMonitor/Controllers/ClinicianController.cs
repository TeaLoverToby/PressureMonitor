using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using System.Security.Claims;

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

        clinician.AllUsers = context.Users.ToList();
        clinician.AllPatientUsers = clinician.AllUsers.Where(u => u.UserType == 0).ToList();
        clinician.SelectedUserItem = new SelectListItem("", "0");



        return View(clinician);
    }

    [HttpPost]
    public async Task<IActionResult> UserSelected(Clinician clinician)
    {
        Clinician c = context.Clinicians
            .Include(c => c.User)
            .FirstOrDefault(c => c.Id == clinician.Id);

        clinician.Id = c.Id;
        clinician.UserId = c.UserId;
        clinician.User = c.User;
        clinician.LicenseNumber = c.LicenseNumber;

        Patient patient = context.Patients.FirstOrDefault(p => p.UserId.ToString() == clinician.SelectedUserItem.Value);
        c.Patients.Clear();
        c.Patients.Add(patient);

        //clinician.Patients = context.Users.ToList();
        //clinician.User = User;

        clinician.AllUsers = context.Users.ToList();
        clinician.AllPatientUsers = clinician.AllUsers.Where(u => u.UserType == 0).ToList();
        clinician.SelectedUserItem = new SelectListItem("", "0");

        return View("index", clinician);
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
        var clinician = await context.Clinicians
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId);
        if (clinician == null)
        {
            return RedirectToAction("Login", "Account");
        }

        // Check that this patient is assigned to this clinician
        var patient = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            .FirstOrDefaultAsync(p => p.Id == patientId); // re-add later

        if (patient == null)
        {
            TempData["Error"] = "Patient not found.";
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

    [HttpGet]
    public async Task<IActionResult> TestSelection()
    {
        // Get the logged in clinician's user ID
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // If not found, just go back to login
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        // Get the clinician record
        var clinician = await context.Clinicians.Include(c => c.User).FirstOrDefaultAsync(c => c.UserId == userId);
        if (clinician == null)
        {
            return RedirectToAction("Login", "Account");
        }
        // Load all the existing patients (for demo version, we load all patients)
        var patients = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            //.Where(p => p.ClinicianId == clinician.Id) // re-add later
            .ToListAsync();
        ViewBag.Patients = patients;
        return View();
    }
}

