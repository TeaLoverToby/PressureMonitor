using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using System.Security.Claims;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(ILogger<AdminController> logger, ApplicationDbContext context) : Controller
{
    [HttpPost]
    public async Task<IActionResult> UserSelected(Admin admin)
    {

        User User = context.Users.FirstOrDefault(u => u.Id.ToString() == admin.SelectedUserItem.Value);
        admin.AllUsers = context.Users.ToList();
        admin.User = User;

        return View("index", admin);
    }

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

        admin.AllUsers = context.Users.ToList();
        admin.SelectedUserItem = new SelectListItem("","0");
        return View(admin);
    }
 }