using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using Microsoft.Data.Sqlite;

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
                        (UserId, MapId, ParentId, Text, CreatedAt)
                        VALUES 
                        ($userId, $mapId, $parentId, $text, $createdAt)
                    ";
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$mapId", comment.MapId);

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
    
    [HttpGet]

    [Route("get-recent-comments")]
    public IActionResult GetRecentComments()
    {
        // list to hold our comments
        var comments = new List<Comment>();

        try
        {
            // connect to database
            using (var connection = new SqliteConnection("Data Source=PressureMonitor.db"))
            {
                connection.Open();

                // create an SQL query to get the 10 most recent comments
                var command = connection.CreateCommand();

                command.CommandText =
                    @"
                SELECT Id, UserId, MapId, ParentId, Text, CreatedAt
                FROM Comments
                ORDER BY CreatedAt DESC
                LIMIT 10
            ";

                // execute the query and read the results
                using (var reader = command.ExecuteReader())
                {
                    // Loop through every row returned by the database
                    while (reader.Read())
                    {
                        // convert database row into c# object
                        comments.Add(new Comment
                        {
                            Id = reader.GetInt32(0),

                            UserId = reader.GetInt32(1),

                            MapId = reader.GetInt32(2),

                            // set parentId to null if null in database
                            ParentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),

                            Text = reader.GetString(4),

                            //convert createdAt string to dateTime
                            CreatedAt = DateTime.Parse(reader.GetString(5))
                        });
                    }
                }
            }


            return Ok(comments);
        }
        //error handling
        catch (Exception ex)
        {
            return BadRequest("DB ERROR: " + ex.Message);
        }
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

