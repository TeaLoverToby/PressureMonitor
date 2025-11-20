using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

public class FileController(ILogger<FileController> logger, ApplicationDbContext context) : Controller
{
    // Constants
    private const int FramesPerSecond = 15;
        
        
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Test()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Home");
        }
        
        // Gets the patient
        var patient = await context.Patients
            .Include(p => p.User)
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null)
        {
            TempData["Error"] = "You must be a patient to access this page.";
            return RedirectToAction("Index", "Home");
        }
        
        return View(patient);
    }
    
    [HttpPost]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        // Get the user ID from the cookie
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            TempData["Error"] = "Authentication error. Please log in again.";
            return RedirectToAction("Login", "Home");
        }

        // Load the patient from the database
        var patient = await context.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null)
        {
            TempData["Error"] = "You must be a patient to upload files.";
            return RedirectToAction(nameof(Test));
        }

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file to upload.";
            return RedirectToAction(nameof(Test));
        }

        if (!file.FileName.EndsWith(".csv"))
        {
            TempData["Error"] = "Only CSV files are allowed.";
            return RedirectToAction(nameof(Test));
        }

        try
        {
            // First, we need to extract the date from the file name
            // Format will be ID_YYYYMMDD;
            string rawName = Path.GetFileNameWithoutExtension(file.FileName);
            string[] nameParts = rawName.Split('_');
            if (nameParts.Length != 2)
            {
                TempData["Error"] = "Filename format is incorrect. Please use ID_YYYYMMDD.csv format.";
                return RedirectToAction(nameof(Test));
            }

            string datePart = nameParts[1];
            if (!DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None,
                    out DateTime date))
            {
                TempData["Error"] = "Filename format is incorrect. Please use ID_YYYYMMDD.csv format.";
                return RedirectToAction(nameof(Test));
            }

            Console.WriteLine("Debug Date: " + date.ToString("yyyy-MM-dd"));

            // So, what we want to assume is that a CSV file contains 32x32 matricies in a time-order
            // The software is 15FPS, so each second of data is 15 matricies.
            
            const int TIME_DIFF_MILLIS = 1000 / FramesPerSecond;

            int currentRow = 0;
            int[][] currentMatrix = new int[32][];
            for (int i = 0; i < 32; i++)
            {
                currentMatrix[i] = new int[32];
            }
            DateTime lastTime = date;

            // Collect all frames in memory first
            List<PressureFrame> frames = new List<PressureFrame>();

            // Using means that it disposes of the stream when done using it - memory gooood
            using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(',');

                    int currentColumn = 0;
                    foreach (string str in values)
                    {
                        if (!int.TryParse(str, out int result)) break;
                        
                        currentMatrix[currentRow][currentColumn] = result;
                        currentColumn++;
                    }

                    currentRow++;

                    // Check if we've completed a full 32x32 matrix
                    if (currentRow >= 32)
                    {
                        // We have a full matrix, so we can process it
                        // The date is incremented as we assume time-ordered
                        lastTime = lastTime.AddMilliseconds(TIME_DIFF_MILLIS);

                        PressureFrame frame = new PressureFrame()
                        {
                            Timestamp = lastTime,
                            Data = currentMatrix,
                        };

                        frames.Add(frame);

                        // Reset for next matrix
                        currentRow = 0;
                        currentMatrix = new int[32][];
                        for (int i = 0; i < 32; i++)
                        {
                            currentMatrix[i] = new int[32];
                        }
                    }
                }
            }

            if (frames.IsNullOrEmpty())
            {
                TempData["Error"] = "No frames found.";
                return RedirectToAction(nameof(Test));
            }

            var day = DateOnly.FromDateTime(date);
            
            // Since CSV files are assumed to represent a full-day, we will overwrite any existing data for that day if it exists
            var existingMap = await context.PressureMaps.Include(pm => pm.Frames).FirstOrDefaultAsync(pm => pm.PatientId == patient.Id && pm.Day == day);
            if (existingMap != null)
            {
                // The frames are automatically deleted due to the "cascade" rule in ApplicationDBContext
                context.PressureMaps.Remove(existingMap); 
                await context.SaveChangesAsync();
            }
            
            PressureMap map = new PressureMap()
            {
                PatientId = patient.Id,
                Patient = patient,
                Day = day,
                Frames = frames
            };

            try
            {
                await context.PressureMaps.AddAsync(map);
                await context.SaveChangesAsync();
            }
            catch (DbException e)
            {
                TempData["Error"] = "Database error occurred while saving the data.";
                logger.LogError(e, "Database error while saving pressure map for patient " + patient.Id);
                return RedirectToAction(nameof(Test));
            }

            TempData["Success"] = existingMap == null ? $"Uploaded {frames.Count} frames for {day:yyyy-MM-dd}." : $"Overwrote existing data and uploaded {frames.Count} frames for {day:yyyy-MM-dd}.";
            logger.LogInformation("Uploaded {FrameCount} frames for patient {PatientId} (overwrite={Overwrite})", frames.Count, patient.Id, existingMap != null);
            return RedirectToAction(nameof(Test));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading file of name " + file.FileName);
            TempData["Error"] = "An error occurred while uploading the file.";
            return RedirectToAction(nameof(Test));
        }
    }

    [HttpGet]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> getMapDays()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }
        var patient = await context.Patients
            .Include(p => p.PressureMaps)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        if (patient == null) return NotFound();

        var days = patient.PressureMaps
            .OrderByDescending(pm => pm.Day)
            .Select(pm => pm.Day.ToString("yyyy-MM-dd"))
            .Distinct()
            .ToList();
        return Json(days);
    }

    [HttpGet]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> getAveragePressureMap(string? day)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Check if the user ID stored in the cookie is still valid
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(day))
        {
            // Sends a HTTP 400 error - data was invalid.
            return BadRequest("day parameter required (yyyy-MM-dd)");
        }
        
        // This shouldn't necessarily happen unless the user modifies the URL
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        // Get the patient and their pressure maps
        var patient = await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null) return NotFound();

        // Check if there is a pressure map that matches the requested day
        var map = patient.PressureMaps.FirstOrDefault(pm => pm.Day == dateOnly);
        if (map == null) return Json(Array.Empty<object>()); // No data!
        
        return Json(new
        {
            averageMap = map.GetAveragePressureMap()
        });
    }

    [HttpGet]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> getPressureGraphData(string? day, int? hoursBack, string? from, string? to)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Check if the user ID stored in the cookie is still valid
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        
        if (string.IsNullOrWhiteSpace(day))
        {
            // Sends a HTTP 400 error - data was invalid.
            return BadRequest("day parameter required (yyyy-MM-dd)");
        }
        // This shouldn't necessarily happen unless the user modifies the URL
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        // Get the patient and their pressure maps
        var patient = await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null) return NotFound();

        // Check if there is a pressure map that matches the requested day
        var map = patient.PressureMaps.FirstOrDefault(pm => pm.Day == dateOnly);
        if (map == null) return Json(Array.Empty<object>()); // No data!

        // This is th range that the user will filter by (initially the full day)
        var dayStart = new DateTime(map.Day.Year, map.Day.Month, map.Day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        DateTime rangeStart = dayStart;
        DateTime rangeEnd = dayEnd;

        // Hours back means how many hours we go back from the end of the day
        // FOr example, if hoursBack was 2, and day was 2025-10-18, then the range would be 2024-10-18 22:00 to 2024-10-18 00:00
        if (hoursBack.HasValue && hoursBack.Value > 0)
        {
            rangeStart = dayEnd.AddHours(-hoursBack.Value);
            if (rangeStart < dayStart) rangeStart = dayStart;
        }
        // This is for the from/to timestamps
        else if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
        {
            if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var parsedFrom))
            {
                if (parsedFrom >= dayStart && parsedFrom < dayEnd) rangeStart = parsedFrom;
            }
            if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var parsedTo))
            {
                if (parsedTo > rangeStart && parsedTo <= dayEnd) rangeEnd = parsedTo;
            }
        }

        // Get the frames within the timestamp range
        var filteredFrames = map.Frames
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .OrderBy(f => f.Timestamp)
            .ToList();

        // SO, the frames are grouped by minute, and for each minute we take the highest pressure between all frames in that minute
        // This gives a list of points with t (time) and v (value)
        // The time is then formatted with ISO format ("o")
        var framePoints = filteredFrames
              .GroupBy(f => new DateTime(f.Timestamp.Year, f.Timestamp.Month, f.Timestamp.Day, f.Timestamp.Hour, f.Timestamp.Minute, 0))
              .Select(g => new { 
                  t = g.Key.ToString("o"), 
                  v = g.Max(f => f.PeakPressure) 
              })
              .ToList();
          

        // Returned as JSON so the graph can parse it
        return Json(new {
            day = map.Day.ToString("yyyy-MM-dd"),
            rangeStart = rangeStart.ToString("o"),
            rangeEnd = rangeEnd.ToString("o"),
            points = framePoints
        });
    }


}