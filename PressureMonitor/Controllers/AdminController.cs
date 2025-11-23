using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(ILogger<AdminController> logger, ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        var admin = await context.Admins
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (admin == null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(admin);
    }
}

