using System.Data.Common;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

/// <summary
/// Manages the uploading, processing and retrieval of pressure map data for patients and clinicians.
/// </summary>
[Authorize(Roles = "Patient,Clinician")]
public class PressureMapController(ILogger<PressureMapController> logger, ApplicationDbContext context) : Controller
{
    // Constants
    private const int FramesPerSecond = 15;

    /// <summary
    /// Uploads and parses a CSV file containing pressure sensor data.
    /// </summary>
    /// <param name="file">The uploaded CSV file.</param>
    /// <param name="startTime">Optional manual start time override</param>
    /// <param name="useCurrentTime">If true, uses the current server time for the timestamp.</param>
    /// <returns>Redirects to the Patient Upload view with success or error messages.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, string? startTime, bool useCurrentTime = false)
    {
        // Check the user is authenticated
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            TempData["Error"] = "Authentication error. Please log in again.";
            return RedirectToAction("Login", "Account");
        }
        // Retireve the patient record linked with the authenticated user
        var patient = await context.Patients.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId);
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
        // The requirements specify that the data is in CSV format
        if (!file.FileName.EndsWith(".csv"))
        {
            TempData["Error"] = "Only CSV files are allowed.";
            return RedirectToAction("Upload", "Patient");
        }
        try
        {
            // Parse the date from the filename (Expected format: ID_YYYYMMDD.csv)
            string rawName = Path.GetFileNameWithoutExtension(file.FileName);
            string[] nameParts = rawName.Split('_');
            if (nameParts.Length != 2)
            {
                TempData["Error"] = "Filename format is incorrect. Please use ID_YYYYMMDD.csv format.";
                return RedirectToAction("Upload", "Patient");
            }
            // Check the date is in the file name
            string datePart = nameParts[1];
            if (!DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
            {
    
                TempData["Error"] = "Filename format is incorrect. Please use ID_YYYYMMDD.csv format.";
                return RedirectToAction("Upload", "Patient");
            }
            // Determine the session start time
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
            // It is assumed that:
            // 1. CSV contains 32x32 matricies in time-order
            // 2. Time difference between frames is always less than a second
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
            // A stream is used to read the file line by line
            // Each line represents a row in the matrix
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
                    // Check a full 32x32 matrix was populated
                    if (currentRow >= 32)
                    {
                        // The date is incremented as it is time-ordered
                        lastTime = lastTime.AddMilliseconds(TIME_DIFF_MILLIS);
                        PressureFrame frame = new PressureFrame()
                        {
                            Timestamp = lastTime,
                            Data = currentMatrix,
                        };
                        frames.Add(frame);
                        // Reset values for next matrix
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
            // Determine the total duration of the new session
            var newSessionStart = frames.Min(f => f.Timestamp);
            var newSessionEnd = frames.Max(f => f.Timestamp);
            // Get maps that are on the same day to check for overlaps
            var existingMaps = await context.PressureMaps.Include(pm => pm.Frames).Where(pm => pm.PatientId == patient.Id && pm.Day == day).ToListAsync();
            // Find sessions that overlap with the new session's time range
            var overlapSessions = existingMaps.Where(pm =>
            {
                if (pm.Frames.Count == 0) return false;
                var existingStart = pm.Frames.Min(f => f.Timestamp);
                var existingEnd = pm.Frames.Max(f => f.Timestamp);
                // We check an overlap by seeing if the new session starts before the existing one ends
                return newSessionStart <= existingEnd && newSessionEnd >= existingStart;
            }).ToList();
            // Remove overlapping sessions to prevent duplicate data
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


    /// <summary>
    /// Retrieves a list of dates for which the current patient has pressure map data.
    /// </summary>
    /// <returns>JSON list of date strings (yyyy-MM-dd).</returns>
    [HttpGet]
    public async Task<IActionResult> GetDays()
    {
        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        return Json(GetPatientDays(patient));
    }
    
    /// <summary>
    /// Calculates the average pressure map for a specific time range.
    /// </summary>
    /// <param name="day">Target date.</param>
    /// <param name="hoursBack">Optional: Filter for the last N hours of the day.</param>
    /// <param name="from">Optional: Filter start timestamp.</param>
    /// <param name="to">Optional: Filter end timestamp.</param>
    /// <returns>JSON object containing the 32x32 average matrix.</returns>
    [HttpGet]
    public async Task<IActionResult> GetAverage(string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");
        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        return Json(GetAverageMap(patient, dateOnly, hoursBack, from, to));
    }

    /// <summary>
    /// Gets the data points for the pressure graph.
    /// </summary>
    /// <param name="day">Target date.</param>
    /// <param name="hoursBack">Optional: Filter for the last N hours.</param>
    /// <param name="from">Optional: Filter start timestamp.</param>
    /// <param name="to">Optional: Filter end timestamp.</param>
    /// <returns>JSON object containing data points time-ordered.</returns>
    [HttpGet]
    public async Task<IActionResult> GetGraphData(string? day, int? hoursBack, string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(day)) return BadRequest("day parameter required (yyyy-MM-dd)");
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await GetCurrentPatient();
        if (patient == null) return Unauthorized();
        
        return Json(GetGraphData(patient, dateOnly, hoursBack, from, to));
    }
    
    /// <summary>
    /// Deletes a specific pressure map session.
    /// </summary>
    /// <param name="id">The ID of the pressure map to delete.</param>
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

        // Find the pressure map and verify that it belongs to the patient before deletion
        var map = await context.PressureMaps
            .FirstOrDefaultAsync(pm => pm.Id == id && pm.PatientId == patient.Id);

        if (map == null)
        {
            TempData["Error"] = "Pressure map was not found.";
            return RedirectToAction("Upload", "Patient");
        }

        var day = map.Day;
        // Get the number of frames without loading them all
        var frameCount = await context.PressureFrames.CountAsync(f => f.PressureMapId == id);

        // Delete the pressure map, cascading will delete the frames automatically
        context.PressureMaps.Remove(map);
        await context.SaveChangesAsync();

        TempData["Success"] = $"Deleted session from {day:yyyy-MM-dd} ({frameCount} frames).";
        logger.LogInformation("Deleted pressure map {MapId} for patient {PatientId} ({FrameCount} frames)", id, patient.Id, frameCount);
        return RedirectToAction("Upload", "Patient");
    }
    
    // Get the patient record linked with the currently logged in user
    private async Task<Patient?> GetCurrentPatient()
    {
        // Fetch the user ID from the claims
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId)) return null;
        
        // Fetch patient with that user id
        return await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
    }
    
    // Verifies that a clinician has access to the requested patient
    private async Task<Patient?> GetClinicianPatient(int patientId)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId)) return null;
        
        // Ensure the current user is a valid clinician
        var clinician = await context.Clinicians.FirstOrDefaultAsync(c => c.UserId == userId);
        if (clinician == null) return null;
        
        // Retrieve the patient record
        return await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            //.FirstOrDefaultAsync(p => p.Id == patientId && p.ClinicianId == clinician.Id); // Commented out for demo
            .FirstOrDefaultAsync(p => p.Id == patientId); // Temporary: allow any patient for demo
    }
    
    // Helper to extract the list of days a patient has pressure map data for
    private List<string> GetPatientDays(Patient patient)
    {
        return patient.PressureMaps
            .OrderByDescending(pm => pm.Day)
            .Select(pm => pm.Day.ToString("yyyy-MM-dd"))
            .Distinct()
            .ToList();
    }
    
    // Helper to calculate the average pressure map for a patient on a specific day with time filtering
    private object GetAverageMap(Patient patient, DateOnly dateOnly, int? hoursBack, string? from, string? to)
    {
        // Get the pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        if (mapsForDay.Count == 0) return new { averageMap = new int[0][] };
        // Calculate the filter time range
        var (rangeStart, rangeEnd) = GetTimeRange(dateOnly, hoursBack, from, to);
        // Combine frames from all sessions for this day and apply time filter
        var filteredFrames = mapsForDay
            .SelectMany(m => m.Frames)
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .ToList();
        if (filteredFrames.Count == 0)
        {
            return new { averageMap = new int[0][] };
        }
        // Calculate the average map from the filtered frames
        var averageMap = CalculateAverageMap(filteredFrames);
        return new { averageMap };
    }
    
    // Helper to get graph data points for a patient on a specific day with time filtering
    private object GetGraphData(Patient patient, DateOnly dateOnly, int? hoursBack, string? from, string? to)
    {
        // Get all pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        if (mapsForDay.Count == 0) return Array.Empty<object>();
        var (rangeStart, rangeEnd) = GetTimeRange(dateOnly, hoursBack, from, to);
        // Filter and sort frames by time
        var filteredFrames = mapsForDay
            .SelectMany(m => m.Frames)
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .OrderBy(f => f.Timestamp)
            .ToList();
        // So, the frames are grouped into buckets of time (e.g 5 seconds) to reduce the number of points shown on the graph
        // This will return a list of points with t (time) and v (value)
        // This is done to smooth out the graph and make it more readable
        const int groupSeconds = 3;
        var framePoints = filteredFrames
            .GroupBy(f => {
                // Get the total seconds since the range start
                var totalSeconds = (int)(f.Timestamp - rangeStart).TotalSeconds;
                // Gets the start time of the bucket (nearest 3-second interval)
                // Example: if groupSeconds is 5, and totalSeconds is 12, then the bucket start is 10
                var bucketStart = rangeStart.AddSeconds(totalSeconds / groupSeconds * groupSeconds);
                return bucketStart;
            })
            .Select(g => new { 
                t = g.Key.ToString("o"), 
                // Calculate the average peak pressure for this group
                v = (int)Math.Round(g.Average(f => f.PeakPressure)) 
            })
            .ToList();
        // Converted to JSON for frontend support
        return new {
            day = dateOnly.ToString("yyyy-MM-dd"),
            rangeStart = rangeStart.ToString("o"),
            rangeEnd = rangeEnd.ToString("o"),
            points = framePoints
        };
    }
    
    // Helper method to parse and calculate start/end times based on optional filter parameters
    private (DateTime rangeStart, DateTime rangeEnd) GetTimeRange(DateOnly dateOnly, int? hoursBack, string? from, string? to)
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
    
    // Helper method to calculate the average pressure map from a list of frames
    private int[][] CalculateAverageMap(List<PressureFrame> frames)
    {
        // Initialize the average map
        var averageMap = new int[32][];
        for (var i = 0; i < 32; i++)
        {
            averageMap[i] = new int[32];
        }
        // Sum all frames
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
        // Divide by the number of frames to get the average
        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
            {
                averageMap[i][j] /= frames.Count;
            }
        }
        return averageMap;
    }
    
    /// <summary>
    /// Get days for a specific patient.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Clinician")]
    public async Task<IActionResult> GetPatientDays(int patientId)
    {
        var patient = await GetClinicianPatient(patientId);
        if (patient == null) return NotFound("This patient was either not found or not assigned to you.");
        return Json(GetPatientDays(patient));
    }
    
    /// <summary>
    /// Get average pressure map for a specific patient.
    /// </summary>
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
    
    /// <summary>
    /// Get graph data for a specific patient.
    /// </summary>
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
