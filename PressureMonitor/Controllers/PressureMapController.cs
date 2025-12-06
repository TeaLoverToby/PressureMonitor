using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Patient")]
public class PressureMapController(ILogger<PressureMapController> logger, ApplicationDbContext context) : Controller
{
    // Constants
    private const int FramesPerSecond = 15;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, string? startTime, bool useCurrentTime = false)
    {
        // Get the user ID from the cookie
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            TempData["Error"] = "Authentication error. Please log in again.";
            return RedirectToAction("Login", "Account");
        }

        // Load the patient from the database
        var patient = await context.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null)
        {
            TempData["Error"] = "You must be a patient to upload files.";
            return RedirectToAction("Upload", "Patient");
        }

        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file to upload.";
            return RedirectToAction("Upload", "Patient");
        }

        if (!file.FileName.EndsWith(".csv"))
        {
            TempData["Error"] = "Only CSV files are allowed.";
            return RedirectToAction("Upload", "Patient");
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
                return RedirectToAction("Upload", "Patient");
            }

            string datePart = nameParts[1];
            if (!DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None,
                    out DateTime date))
            {
                TempData["Error"] = "Filename format is incorrect. Please use ID_YYYYMMDD.csv format.";
                return RedirectToAction("Upload", "Patient");
            }

            // Get the start time for the session
            DateTime sessionStartTime;
            if (useCurrentTime)
            {
                // Use current time but keep the date from the file name
                var now = DateTime.Now;
                sessionStartTime = new DateTime(date.Year, date.Month, date.Day, now.Hour, now.Minute, now.Second);
            }
            else if (!string.IsNullOrWhiteSpace(startTime) && TimeOnly.TryParse(startTime, out var parsedTime))
            {
                sessionStartTime = new DateTime(date.Year, date.Month, date.Day, parsedTime.Hour, parsedTime.Minute, 0);
            }
            else
            {
                // Default to midnight if there was no specified time
                sessionStartTime = date;
            }

            // So, what we want to assume is that a CSV file contains 32x32 matricies in a time-order
            // The software is 15FPS, so each second of data is 15 matricies.
            
            const int TIME_DIFF_MILLIS = 1000 / FramesPerSecond;

            int currentRow = 0;
            int[][] currentMatrix = new int[32][];
            for (int i = 0; i < 32; i++)
            {
                currentMatrix[i] = new int[32];
            }
            DateTime lastTime = sessionStartTime;

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
                        
                        // Must be within valid range (0-255)
                        result = Math.Clamp(result, 0, 255);
                        
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
                return RedirectToAction("Upload", "Patient");
            }

            var day = DateOnly.FromDateTime(date);
            
            // Need to get the start and end time for the session
            var newSessionStart = frames.Min(f => f.Timestamp);
            var newSessionEnd = frames.Max(f => f.Timestamp);
            
            // Get maps that are on the same day
            var existingMaps = await context.PressureMaps
                .Include(pm => pm.Frames)
                .Where(pm => pm.PatientId == patient.Id && pm.Day == day)
                .ToListAsync();
            
            // Findsessions  that overlap swith the new session's time range
            var overlapSessions = existingMaps.Where(pm =>
            {
                if (pm.Frames.Count == 0) return false;
                var existingStart = pm.Frames.Min(f => f.Timestamp);
                var existingEnd = pm.Frames.Max(f => f.Timestamp);
                // We check an overlap by seeing if the new session starts before the existing one ends
                return newSessionStart <= existingEnd && newSessionEnd >= existingStart;
            }).ToList();
            
            // Remove overlapping sessions
            if (overlapSessions.Count > 0)
            {
                foreach (var overlapping in overlapSessions)
                {
                    context.PressureMaps.Remove(overlapping);
                }
                await context.SaveChangesAsync();
                logger.LogInformation("Removed {Count} overlapping session(s) for patient {PatientId} on {Day}", overlapSessions.Count, patient.Id, day);
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
                return RedirectToAction("Upload", "Patient");
            }

            var endTimeStr = newSessionEnd.ToString("HH:mm");
            var startTimeStr = newSessionStart.ToString("HH:mm");
            TempData["Success"] = $"Uploaded {frames.Count} frames for {day:yyyy-MM-dd} ({startTimeStr} - {endTimeStr}).";
            logger.LogInformation("Uploaded {FrameCount} frames for patient {PatientId} on {Day} starting at {StartTime}", frames.Count, patient.Id, day, sessionStartTime);
            return RedirectToAction("Upload", "Patient");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading file of name " + file.FileName);
            TempData["Error"] = "An error occurred while uploading the file.";
            return RedirectToAction("Upload", "Patient");
        }
    }


    // Will write proper comments later but this is meant to act as a "wrapper" for the shared logic
    [HttpGet]
    public async Task<IActionResult> GetDays()
    {
        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        return Json(GetPatientDays(patient));
    }
    
    // This gets the average pressure map but does some patient validation first
    [HttpGet]
    public async Task<IActionResult> GetAverage(string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        
        return Json(GetAverageMap(patient, dateOnly, hoursBack, from, to));
    }

    // This gets the graph data points but does some patient validation first
    [HttpGet]
    public async Task<IActionResult> GetGraphData(string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        
        return Json(GetGraphData(patient, dateOnly, hoursBack, from, to));
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            TempData["Error"] = "Patient not found. Please log in again.";
            return RedirectToAction("Login", "Account");
        }

        // Get the patient
        var patient = await context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null)
        {
            TempData["Error"] = "Patient not found.";
            return RedirectToAction("Upload", "Patient");
        }

        // Find the pressure map and check that it belongs to the patient (incase it was already deleted)
        var map = await context.PressureMaps
            .Include(pm => pm.Frames)
            .FirstOrDefaultAsync(pm => pm.Id == id && pm.PatientId == patient.Id);

        if (map == null)
        {
            TempData["Error"] = "Pressure map was not found.";
            return RedirectToAction("Upload", "Patient");
        }

        var day = map.Day;
        var frameCount = map.Frames.Count;

        // Delete the pressure map
        context.PressureMaps.Remove(map);
        await context.SaveChangesAsync();

        TempData["Success"] = $"Deleted session from {day:yyyy-MM-dd} ({frameCount} frames).";
        logger.LogInformation("Deleted pressure map {MapId} for patient {PatientId} ({FrameCount} frames)", id, patient.Id, frameCount);
        return RedirectToAction("Upload", "Patient");
    }
    
    // Get the current user's patient record (they must be logged in)
    private async Task<Patient?> GetCurrentPatient()
    {
        // Get the user ID from the claims
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId)) return null;
        
        // Load the patient with that user id
        return await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
    }
    
    // This acts as a way of checking that a clinician can only access their assigned patients
    private async Task<Patient?> GetClinicianPatient(int patientId)
    {
        // Get the user ID from the claims
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId)) return null;
        
        // Checks if there is a clinician with the user id (to prevent smelly liers)
        var clinician = await context.Clinicians.FirstOrDefaultAsync(c => c.UserId == userId);
        if (clinician == null) return null;
        
        //Get the patients who have this clinician assigned
        // TODO: Might be able to use Clinician.Patients instead???
        return await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.Id == patientId && p.ClinicianId == clinician.Id);
    }
    
    // Seperated into a method to prevent code duplication
    private List<string> GetPatientDays(Patient patient)
    {
        return patient.PressureMaps
            .OrderByDescending(pm => pm.Day)
            .Select(pm => pm.Day.ToString("yyyy-MM-dd"))
            .Distinct()
            .ToList();
    }
    
    private object GetAverageMap(Patient patient, DateOnly dateOnly, int? hoursBack, string? from, string? to)
    {
        // Get the pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        // Empty result if no maps
        if (mapsForDay.Count == 0) return new { averageMap = new int[0][] };
        
        // Calculate and return the time range
        var (rangeStart, rangeEnd) = getTimeRange(dateOnly, hoursBack, from, to);
        
        // Combine frames from all sessions for this day and filter by time range
        var filteredFrames = mapsForDay
            .SelectMany(m => m.Frames)
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .ToList();

        // If no frames in range, return empty array
        if (filteredFrames.Count == 0)
        {
            return new { averageMap = new int[0][] };
        }

        // TODO: Moved the calculation code to its own method
        // Calculate the average map from the filtered frames
        var averageMap = CalculateAverageMap(filteredFrames);
        return new { averageMap };
    }
    
    private object GetGraphData(Patient patient, DateOnly dateOnly, int? hoursBack, string? from, string? to)
    {
        // Get all pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        if (mapsForDay.Count == 0) return Array.Empty<object>();

        // This is the range that the user will filter by (initially the full day)
        var (rangeStart, rangeEnd) = getTimeRange(dateOnly, hoursBack, from, to);

        // Combine frames from all sessions for this day then filter by time range
        var filteredFrames = mapsForDay
            .SelectMany(m => m.Frames)
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .OrderBy(f => f.Timestamp)
            .ToList();

        // SO, the frames are grouped by minute, and for each minute we take the highest pressure between all frames in that minute
        // This gives a list of points with t (time) and v (value)
        // The time is then formatted with ISO format ("o")
        var framePoints = filteredFrames
            .GroupBy(f => new DateTime(f.Timestamp.Year, f.Timestamp.Month, f.Timestamp.Day, f.Timestamp.Hour, f.Timestamp.Minute, 0))
            .Select(g => new { t = g.Key.ToString("o"), v = g.Max(f => f.PeakPressure) })
            .ToList();

        // This can be converted to JSON later sp gra[h can parse it]
        return new {
            day = dateOnly.ToString("yyyy-MM-dd"),
            rangeStart = rangeStart.ToString("o"),
            rangeEnd = rangeEnd.ToString("o"),
            points = framePoints
        };
    }
    
    // New helper method since time range is used in multiple places
    private (DateTime rangeStart, DateTime rangeEnd) getTimeRange(DateOnly dateOnly, int? hoursBack, string? from, string? to)
    {
        // This is the range that the user will filter by (initially the full day)
        var dayStart = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        var rangeStart = dayStart;
        var rangeEnd = dayEnd;

        // Hours back means how many hours we go back from the end of the day
        // For example, if hoursBack was 2, and day was 2025-10-18, then the range would be 2024-10-18 22:00 to 2024-10-18 00:00
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

        return (rangeStart, rangeEnd);
    }
    

    // TODO: Maybe I should look to optimize by storing the average map for each time frame (1h, 6h, etc)?
    private int[][] CalculateAverageMap(List<PressureFrame> frames)
    {
        var averageMap = new int[32][];
        for (var i = 0; i < 32; i++)
        {
            averageMap[i] = new int[32];
        }

        foreach (var frame in frames)
        {
            var data = frame.Data;
            for (var i = 0; i < 32; i++)
            {
                for (var j = 0; j < 32; j++)
                {
                    averageMap[i][j] += data[i][j];
                }
            }
        }

        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
            {
                averageMap[i][j] /= frames.Count;
            }
        }
        return averageMap;
    }
    
    // TODO: Jack, this will be useful for you as it allows clinicians to get data for their patients
    [HttpGet]
    [Authorize(Roles = "Clinician")]
    public async Task<IActionResult> GetPatientDays(int patientId)
    {
        var patient = await GetClinicianPatient(patientId);
        if (patient == null) return NotFound("This patient was either not found or not assigned to you.");
        return Json(GetPatientDays(patient));
    }
    
    [HttpGet]
    [Authorize(Roles = "Clinician")]
    public async Task<IActionResult> GetPatientAverage(int patientId, string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await GetClinicianPatient(patientId);
        if (patient == null) return NotFound("This patient was either not found or not assigned to you.");
        
        return Json(GetAverageMap(patient, dateOnly, hoursBack, from, to));
    }
    
    [HttpGet]
    [Authorize(Roles = "Clinician")]
    public async Task<IActionResult> GetPatientGraphData(int patientId, string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await GetClinicianPatient(patientId);
        if (patient == null) return NotFound("This patient was either not found or not assigned to you.");
        
        return Json(GetGraphData(patient, dateOnly, hoursBack, from, to));
    }
}
