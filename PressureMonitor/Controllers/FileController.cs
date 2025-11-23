using System.Security.Claims;
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
}

