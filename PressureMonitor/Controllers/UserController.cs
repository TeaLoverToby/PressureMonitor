using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using System.Security.Claims;

namespace PressureMonitor.Controllers;

public class UserController : Controller
{
    private readonly ApplicationDbContext _context;
    
    public UserController(ApplicationDbContext context)
    {
        _context = context;
    }
    
    [Authorize]
    public IActionResult Dashboard()
    {
        var userTypeStr = User.FindFirst("UserType")?.Value;
        
        if (Enum.TryParse<UserType>(userTypeStr, out var userType))
        {
            return userType switch
            {
                UserType.Patient => RedirectToAction(nameof(PatientDashboard)),
                UserType.Clinician => RedirectToAction(nameof(ClinicianDashboard)),
                UserType.Admin => RedirectToAction(nameof(AdminDashboard)),
                _ => RedirectToAction("Index", "Home")
            };
        }
        
        return RedirectToAction("Index", "Home");
    }
    
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> PatientDashboard()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        // We basically check if there is a user where the patient entity is not null - meaning it is a patient.
        var user = await _context.Users.Include(u => u.Patient).FirstOrDefaultAsync(u => u.Id == userId);
        
        if (user?.Patient == null)
        {
            ViewBag.Message = "Patient profile not found. Please logout and sign in with a valid account.";
            return View("RoleMissing");
        }
        
        return View(user.Patient);
    }
    
    [Authorize(Roles = "Clinician")]
    public async Task<IActionResult> ClinicianDashboard()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var user = await _context.Users.Include(u => u.Clinician).FirstOrDefaultAsync(u => u.Id == userId);
        
        if (user?.Clinician == null)
        {
            ViewBag.Message = "Clinician profile not found. Please logout and sign in with a valid account.";
            return View("RoleMissing");
        }
        
        ViewData["LicenseNumber"] = user.Clinician.LicenseNumber;
        
        return View(user.Clinician);
    }
    
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminDashboard()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var user = await _context.Users.Include(u => u.Admin).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || user.UserType != UserType.Admin || user.Admin == null)
        {
            ViewBag.Message = "Admin profile not found. Please logout and sign in with a valid account.";
            return View("RoleMissing");
        }
        
        return View(user.Admin);
    }
}
