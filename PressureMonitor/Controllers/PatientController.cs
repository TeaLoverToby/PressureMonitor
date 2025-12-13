using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using Microsoft.Data.Sqlite;

namespace PressureMonitor.Controllers;


/// <summary>
/// Controller for Patient-specific actions (Dashboard, Uploads, Comments)
/// </summary>
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

    [HttpPost]
    [Route("save-comment")]
    public IActionResult SaveComment([FromBody] Comment comment)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            using (var connection = new SqliteConnection("Data Source=PressureMonitor.db"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText =
                    @"
                        INSERT INTO Comments 
                        (UserId, ParentId, Text, CreatedAt)
                        VALUES 
                        ($userId, $parentId, $text, $createdAt)
                    ";
                command.Parameters.AddWithValue("$userId", userId);

                if (comment.ParentId == null)
                    command.Parameters.AddWithValue("$parentId", DBNull.Value);
                else
                    command.Parameters.AddWithValue("$parentId", comment.ParentId);

                command.Parameters.AddWithValue("$text", comment.Text);
                command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);

                command.ExecuteNonQuery();
            }

            return Ok("Insert successful");
        }
        catch (Exception ex)
        {
            return BadRequest("DB ERROR: " + ex.Message);
        }
    } 
    
    [HttpGet("/get-comments-by-day")]
    public IActionResult GetCommentsByDay(string day)
    {
        if (string.IsNullOrWhiteSpace(day))
            return BadRequest("Missing day");

        // Parse the day into a DateTime
        if (!DateTime.TryParse(day, out var targetDate))
            return BadRequest("Invalid date format");

        var comments = new List<object>(); // anonymous objects to include username

        try
        {
            using (var connection = new SqliteConnection("Data Source=PressureMonitor.db"))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                SELECT c.Id, c.UserId, u.Username AS UserName, c.ParentId, c.Text, c.CreatedAt
                FROM Comments c
                JOIN Users u ON u.Id = c.UserId
                WHERE date(c.CreatedAt) = date($day)
                ORDER BY c.CreatedAt DESC
            ";

                command.Parameters.AddWithValue("$day", targetDate.ToString("yyyy-MM-dd"));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        comments.Add(new
                        {
                            id = reader.GetInt32(0),
                            userId = reader.GetInt32(1),
                            userName = reader.GetString(2),
                            parentId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                            text = reader.GetString(4),
                            createdAt = reader.GetString(5)
                        });
                    }
                }
            }

            return Ok(comments);
        }
        catch (Exception ex)
        {
            return BadRequest("DB ERROR: " + ex.Message);
        }
    }
    
    /// <summary>
    /// Displays the Upload view, allows patients to upload their pressure map data.
    /// </summary>
    /// <returns>The Upload view showing existing pressure maps for the patient.</returns>
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

    /// <summary>
    /// View the pressure map session for a specific day.
    /// </summary>
    /// <param name="day">The date of the session to view.</param>
    /// <returns>The ViewPressureMap view with session data.</returns>
    [HttpGet]
    public async Task<IActionResult> ViewPressureMap(string day)
    {
        // Get the logged in patient's user ID
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Account");
        }

        // Get the patient record
        var patient = await context.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null)
        {
            return RedirectToAction("Login", "Account");
        }

        // Attempt to parse the day parameter
        if (!DateOnly.TryParse(day, out var dayOnly))
        {
            TempData["Error"] = "Invalid day format.";
            return RedirectToAction(nameof(Upload));
        }

        // Get the pressure map for that day
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

