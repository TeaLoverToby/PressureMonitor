using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Patient")]
public class FileController(ILogger<FileController> logger, ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> GetMapDays()
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
    public async Task<IActionResult> GetAveragePressureMap(string? day, int? hoursBack, string? from, string? to)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(day))
        {
            return BadRequest("day parameter required (yyyy-MM-dd)");
        }
        
        if (!DateOnly.TryParse(day, out var dateOnly)) return BadRequest("Invalid day format");

        var patient = await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);
        
        if (patient == null) return NotFound();

        var map = patient.PressureMaps.FirstOrDefault(pm => pm.Day == dateOnly);
        if (map == null) return Json(new { averageMap = new int[0][] });
        
        // Calculate time range (same logic as getPressureGraphData)
        var dayStart = new DateTime(map.Day.Year, map.Day.Month, map.Day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        DateTime rangeStart = dayStart;
        DateTime rangeEnd = dayEnd;

        if (hoursBack.HasValue && hoursBack.Value > 0)
        {
            rangeStart = dayEnd.AddHours(-hoursBack.Value);
            if (rangeStart < dayStart) rangeStart = dayStart;
        }
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

        // Filter frames by time range
        var filteredFrames = map.Frames
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .ToList();

        // If no frames in range, return empty array
        if (filteredFrames.Count == 0)
        {
            return Json(new { averageMap = new int[0][] });
        }

        // Calculate average from filtered frames
        var averageMap = new int[32][];
        for (var i = 0; i < 32; i++)
        {
            averageMap[i] = new int[32];
        }

        foreach (var frame in filteredFrames)
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
                averageMap[i][j] /= filteredFrames.Count;
            }
        }
        
        return Json(new { averageMap });
    }

    [HttpGet]
    public async Task<IActionResult> GetPressureGraphData(string? day, int? hoursBack, string? from, string? to)
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

        // This is the range that the user will filter by (initially the full day)
        var dayStart = new DateTime(map.Day.Year, map.Day.Month, map.Day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        DateTime rangeStart = dayStart;
        DateTime rangeEnd = dayEnd;

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

        // Get the frames within the timestamp range
        var filteredFrames = map.Frames
            .Where(f => f.Timestamp >= rangeStart && f.Timestamp < rangeEnd)
            .OrderBy(f => f.Timestamp)
            .ToList();

        // The frames are grouped by minute, and for each minute we take the highest pressure between all frames in that minute
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
    [HttpGet]
    public async Task<IActionResult> DownloadCsv(string day)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(day) || !DateOnly.TryParse(day, out var dateOnly))
        {
            return BadRequest("Invalid day format");
        }

        // Get the patient and their pressure maps
        var patient = await context.Patients
            .Include(p => p.PressureMaps)
            .ThenInclude(pm => pm.Frames)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null) return NotFound();

        // Find the pressure map for the specified day
        var map = patient.PressureMaps.FirstOrDefault(pm => pm.Day == dateOnly);
        if (map == null) return NotFound("No data found for this day.");

        // Order the frames by their timestamp (ascending)
        var frames = map.Frames.OrderBy(f => f.Timestamp).ToList();

        var sb = new StringBuilder();
        // Iterate through each frame and append its data to the string builder
        foreach (var frame in frames)
        {
            // Check if Data is null, though it shouldn't be for valid frames
            if (frame.Data == null) continue;

            for (int r = 0; r < 32; r++)
            {
                // Join the row values with commas
                sb.AppendLine(string.Join(",", frame.Data[r]));
            }
        }

        var fileName = $"{patient.Id}_{dateOnly:yyyyMMdd}.csv";
        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(fileBytes, "text/csv", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadReport(string day)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(day) || !DateOnly.TryParse(day, out var dateOnly))
        {
            return BadRequest("Invalid day format");
        }

        // Get the patient ID, we do not include pressuremaps for the sake of performance
        var patientId = await context.Patients
            .Where(p => p.UserId == userId)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();

        if (patientId == 0) return NotFound();

        // Get the pressure map for the given day
        var map = await context.PressureMaps
            .Include(pm => pm.Frames)
            .FirstOrDefaultAsync(pm => pm.PatientId == patientId && pm.Day == dateOnly);

        if (map == null) return NotFound("No data found for this day.");

        // Order the frames by timestamp (oldest -> newest)
        var frames = map.Frames.OrderBy(f => f.Timestamp).ToList();
        if (frames.Count == 0)
        {
            // If no frames, we will just build a report with no data
             return File(Encoding.UTF8.GetBytes("No data available for this day."), "text/plain", $"Report_{dateOnly:yyyyMMdd}.txt");
        }

        // Calculate the statistics for the report
        int maxPressure = 0;
        int minPressure = int.MaxValue;
        int maxPeakPressure = 0;
        double totalContactArea = 0;
        long totalPressureSum = 0;
        long totalCellCount = 0;
        
        // To find high pressure regions, we'll average each cell across all frames
        var averageMap = new long[32, 32];

        foreach (var frame in frames)
        {
            if (frame.MaxValue > maxPressure) maxPressure = frame.MaxValue;
            if (frame.MinValue < minPressure) minPressure = frame.MinValue;
            if (frame.PeakPressure > maxPeakPressure) maxPeakPressure = frame.PeakPressure;
            
            totalContactArea += frame.ContactAreaPercentage;

            // We cannot really avoid deserialising the matrix data as we need to access the cells
            
            // TODO: We might be able to store the average data in each frame so we don't have to do this
            var data = frame.Data;
            if (data == null) continue;

            // Iterate through the cells to calculate total pressure         
            for (int r = 0; r < 32; r++)
            {
                for (int c = 0; c < 32; c++)
                {
                    int val = data[r][c];
                    totalPressureSum += val;
                    totalCellCount++;
                    averageMap[r, c] += val;
                }
            }
        }

        double overallAverage = totalCellCount > 0 ? (double)totalPressureSum / totalCellCount : 0;
        double averageContactArea = frames.Count > 0 ? totalContactArea / frames.Count : 0;
        
        var startTime = frames.First().Timestamp;
        var endTime = frames.Last().Timestamp;
        var duration = endTime - startTime;

        // Identify top 10 high pressure regions (cells)
        const int topRegionsCount = 10;
        // Turns out that c# has tuple support!
        var cellAverages = new List<(int r, int c, double avg)>();

        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                // Calculate the average pressure for each cell
                double avg = (double)averageMap[r, c] / frames.Count;
                cellAverages.Add((r, c, avg));
            }
        }

        var topRegions = cellAverages.OrderByDescending(x => x.avg).Take(topRegionsCount).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Pressure Map Report for {dateOnly:yyyy-MM-dd}");
        sb.AppendLine("========================================");
        sb.AppendLine($"Patient ID: {patientId}");
        sb.AppendLine($"Pressure Map ID: {map.Id}");
        sb.AppendLine($"Report Generated: {DateTime.Now}");
        sb.AppendLine("========================================");
        sb.AppendLine($"Recording Start: {startTime:HH:mm:ss}");
        sb.AppendLine($"Recording End: {endTime:HH:mm:ss}");
        sb.AppendLine($"Total Duration: {duration}");
        sb.AppendLine($"Total Frames Recorded: {frames.Count}");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Highest Pressure Found: {maxPressure}");
        sb.AppendLine($"Lowest Pressure Found: {minPressure}");
        sb.AppendLine($"Max Peak Pressure Index: {maxPeakPressure}");
        sb.AppendLine($"Average Pressure: {overallAverage:F2}");
        sb.AppendLine($"Average Contact Area: {averageContactArea:F2}%");
        sb.AppendLine();
        sb.AppendLine($"High Pressure Regions (Top {topRegionsCount} Average Cells):");
        sb.AppendLine("----------------------------------------");
        
        int index = 1;
        foreach (var region in topRegions)
        {
            sb.AppendLine($"{index++}: Row: {region.r}, Column: {region.c} - Average Pressure: {region.avg:F2}");
        }

        // TODO: Need to add some sort of alert or specific details for the report

        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(fileBytes, "text/plain", $"Report_{dateOnly:yyyyMMdd}.txt");
    }
}

