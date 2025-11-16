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
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Test()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login", "Home");
        }

        // PressureMaps are included since we show data (will remove later) for the days
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

            const int FPS = 15;
            const int TIME_DIFF_MILLIS = 1000 / FPS;

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


}