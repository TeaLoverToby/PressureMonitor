using System.Security.Claims;
using DocumentFormat.OpenXml.Vml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
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

        List<UserInfo> UserList = admin.GetUsers;



        return View(admin);
    }

    //class UserListInfo
    //{
    //    public int Id { get; set; }
    //    public string UserName { get; set; } = string.Empty;
    }

    //public List<SelectListItem> UserList
    //{
    //    get
    //    {
    //        List<SelectListItem> users = Admin.

    //    }
    //}

