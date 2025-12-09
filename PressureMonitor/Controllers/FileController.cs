using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PressureMonitor.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WordDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace PressureMonitor.Controllers;

[Authorize(Roles = "Patient,Clinician")]
public class FileController(ILogger<FileController> logger, ApplicationDbContext context) : Controller
{

    [HttpGet]
    public async Task<IActionResult> DownloadCsv(string day, int? patientId = null)
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

        // Get the patient
        // If the patientId was already provided (clinician) then we use that else find by userId (patient)
        var patient = await context.Patients.Include(p => p.PressureMaps).ThenInclude(pm => pm.Frames).FirstOrDefaultAsync(p => patientId.HasValue ? p.Id == patientId.Value : p.UserId == userId);

        if (patient == null) return NotFound();

        // Get all pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        if (mapsForDay.Count == 0) return NotFound("No data found for this day.");

        // We need to combine and order all frames from all sessions by timestamp
        var frames = mapsForDay
            .SelectMany(m => m.Frames)
            .OrderBy(f => f.Timestamp)
            .ToList();

        var sb = new StringBuilder();
        // Iterate through each frame and append its data to the string builder
        foreach (var frame in frames)
        {
            // Check if Data is null, though it shouldn't be for valid frames
            var data = frame.Data;
            if (data == null) continue;
            
            for (int r = 0; r < 32; r++)
            {
                // Join the row values with commas
                sb.AppendLine(string.Join(",", data[r]));
            }
        }

        var fileName = $"{patient.Id}_{dateOnly:yyyyMMdd}.csv";
        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(fileBytes, "text/csv", fileName);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadReport(string day, string format, int? patientId = null)
    {
        // Get the user from the cookie
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(day) || !DateOnly.TryParse(day, out var dateOnly))
        {
            return BadRequest("Invalid day format");
        }

        // Get the patient - if patientId provided (clinician), use that; otherwise find by userId (patient)
        Patient? patient;
        if (patientId.HasValue)
        {
            patient = await context.Patients
                .Include(p => p.PressureMaps)
                .ThenInclude(pm => pm.Frames)
                .FirstOrDefaultAsync(p => p.Id == patientId.Value);
        }
        else
        {
            patient = await context.Patients
                .Include(p => p.PressureMaps)
                .ThenInclude(pm => pm.Frames)
                .FirstOrDefaultAsync(p => p.UserId == userId);
        }

        if (patient == null) return NotFound();

        // Get all pressure maps for the requested day
        var mapsForDay = patient.PressureMaps.Where(pm => pm.Day == dateOnly).ToList();
        if (mapsForDay.Count == 0) return NotFound("No data found for this day.");

        // Calculate statistics from all the sessions/maps
        var (reportData, topRegions) = CalculateReportData(mapsForDay, patient.Id, dateOnly);

        // Generate report based on format
        return format.ToLower() switch
        {
            "docx" => GenerateDocxReport(reportData, topRegions, dateOnly),
            "pdf" => GeneratePdfReport(reportData, topRegions, dateOnly),
            _ => GenerateTxtReport(reportData, topRegions, dateOnly)
        };
    }
    
    private (ReportData data, List<(int r, int c, double avg)> topRegions) CalculateReportData(List<PressureMap> maps, int patientId, DateOnly dateOnly)
    {
        // Combine all frames from all sessions
        var frames = maps
            .SelectMany(m => m.Frames)
            .OrderBy(f => f.Timestamp)
            .ToList();
            
        if (frames.Count == 0)
        {
            // Basically an empty data report
            return (new ReportData
            {
                PatientId = patientId,
                MapList = maps.Select(m => m.Id).ToList(),
                Day = dateOnly,
                FrameCount = 0,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                Duration = TimeSpan.Zero,
                MinPressure = 0,
                MaxPressure = 0,
                OverallAverage = 0,
                MaxPeakPressure = 0,
                AverageContactArea = 0
            }, new List<(int, int, double)>());
        }

      // Calculate the statistics for the report
        int maxPressure = 0;
        int minPressure = int.MaxValue;
        int maxPeakPressure = 0;
        double totalContactArea = 0;
        long totalPressureSum = 0;
        long totalCellCount = 0;

        // To find high pressure regions, we'll need to average each cell across all frames
        var averageMap = new long[32, 32];

        foreach (var frame in frames)
        {
            if (frame.MaxValue > maxPressure) maxPressure = frame.MaxValue;
            if (frame.MinValue < minPressure) minPressure = frame.MinValue;
            if (frame.PeakPressure > maxPeakPressure) maxPeakPressure = frame.PeakPressure;
            totalContactArea += frame.ContactAreaPercentage;

            // We cannot really avoid deserializing the matrix data as we need to access the cells
            // TODO: Might be able to look into storing average data for each frame in the DB
            var data = frame.Data;
            if (data == null) continue;

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

        // Top 10 high pressure regions
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
        // Get the top x regions by average pressure
        var topRegions = cellAverages.OrderByDescending(x => x.avg).Take(topRegionsCount).ToList();

        var reportData = new ReportData
        {
            PatientId = patientId,
            MapList = maps.Select(m => m.Id).ToList(),
            Day = dateOnly,
            FrameCount = frames.Count,
            StartTime = startTime,
            EndTime = endTime,
            Duration = duration,
            MaxPressure = maxPressure,
            MinPressure = minPressure,
            MaxPeakPressure = maxPeakPressure,
            OverallAverage = overallAverage,
            AverageContactArea = averageContactArea
        };

        // TODO: Need to add some sort of alert or specific details for the report

        return (reportData, topRegions);
    }


    private IActionResult GenerateTxtReport(ReportData data, List<(int r, int c, double avg)> topRegions, DateOnly dateOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pressure Map Report for {dateOnly:yyyy-MM-dd}");
        sb.AppendLine("========================================");
        sb.AppendLine($"Patient ID: {data.PatientId}");
        sb.AppendLine($"Sessions: {data.MapList.Count}");
        sb.AppendLine($"Report Generated: {DateTime.Now}");
        sb.AppendLine("========================================");
        sb.AppendLine($"Recording Start: {data.StartTime:HH:mm:ss}");
        sb.AppendLine($"Recording End: {data.EndTime:HH:mm:ss}");
        sb.AppendLine($"Total Duration: {data.Duration}");
        sb.AppendLine($"Total Frames Recorded: {data.FrameCount}");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Highest Pressure Found: {data.MaxPressure}");
        sb.AppendLine($"Lowest Pressure Found: {data.MinPressure}");
        sb.AppendLine($"Max Peak Pressure Index: {data.MaxPeakPressure}");
        sb.AppendLine($"Average Pressure: {data.OverallAverage:F2}");
        sb.AppendLine($"Average Contact Area: {data.AverageContactArea:F2}%");
        sb.AppendLine();
        sb.AppendLine($"High Pressure Regions (Top {topRegions.Count} Average Cells):");
        sb.AppendLine("----------------------------------------");
        
        int index = 1;
        foreach (var region in topRegions)
        {
            sb.AppendLine($"{index++}: Row: {region.r}, Column: {region.c} - Average Pressure: {region.avg:F2}");
        }

        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(fileBytes, "text/plain", $"Report_{dateOnly:yyyyMMdd}.txt");
    }


    //CITE: https://learn.microsoft.com/en-us/office/open-xml/word/how-to-create-a-word-processing-document-by-providing-a-file-name
    private IActionResult GenerateDocxReport(ReportData data, List<(int r, int c, double avg)> topRegions, DateOnly dateOnly)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new WordDocument();
            var body = mainPart.Document.AppendChild(new Body());

            AddParagraph(body, $"Pressure Map Report for {dateOnly:yyyy-MM-dd}");
            AddParagraph(body, "========================================");
            AddParagraph(body, $"Patient ID: {data.PatientId}");
            AddParagraph(body, $"Sessions: {data.MapList.Count}");
            AddParagraph(body, $"Report Generated: {DateTime.Now}");
            AddParagraph(body, "========================================");
            AddParagraph(body, $"Recording Start: {data.StartTime:HH:mm:ss}");
            AddParagraph(body, $"Recording End: {data.EndTime:HH:mm:ss}");
            AddParagraph(body, $"Total Duration: {data.Duration}");
            AddParagraph(body, $"Total Frames Recorded: {data.FrameCount}");
            AddParagraph(body, "----------------------------------------");
            AddParagraph(body, $"Highest Pressure Found: {data.MaxPressure}");
            AddParagraph(body, $"Lowest Pressure Found: {data.MinPressure}");
            AddParagraph(body, $"Max Peak Pressure Index: {data.MaxPeakPressure}");
            AddParagraph(body, $"Average Pressure: {data.OverallAverage:F2}");
            AddParagraph(body, $"Average Contact Area: {data.AverageContactArea:F2}%");
            AddParagraph(body, "");
            AddParagraph(body, $"High Pressure Regions (Top {topRegions.Count} Average Cells):");
            AddParagraph(body, "----------------------------------------");
            
            int index = 1;
            foreach (var region in topRegions)
            {
                AddParagraph(body, $"{index++}: Row: {region.r}, Column: {region.c} - Average Pressure: {region.avg:F2}");
            }
        }

        var fileBytes = stream.ToArray();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Report_{dateOnly:yyyyMMdd}.docx");
    }

    private void AddParagraph(Body body, string text)
    {
        // Similar to HTML, a paragraph represents a body of text
        var paragraph = body.AppendChild(new Paragraph());
        // This holds the specific chunk of text with styling
        var run = paragraph.AppendChild(new Run());
        // Appends it to the paragraph
        run.AppendChild(new Text(text));
    }

    private IActionResult GeneratePdfReport(ReportData data, List<(int r, int c, double avg)> topRegions, DateOnly dateOnly)
    {   
        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Text($"Pressure Map Report for {dateOnly:yyyy-MM-dd}").SemiBold().FontSize(16).FontColor(Colors.White);

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        col.Spacing(5);
                        
                        col.Item().Text("Report Information").SemiBold().FontSize(14);
                        col.Item().Text($"Patient ID: {data.PatientId}");
                        col.Item().Text($"Sessions: {data.MapList.Count}");
                        col.Item().Text($"Report Generated: {DateTime.Now}");
                        
                        col.Item().PaddingTop(10);
                        col.Item().Text("Recording Details").SemiBold().FontSize(14);
                        col.Item().Text($"Recording Start: {data.StartTime:HH:mm:ss}");
                        col.Item().Text($"Recording End: {data.EndTime:HH:mm:ss}");
                        col.Item().Text($"Total Duration: {data.Duration}");
                        col.Item().Text($"Total Frames Recorded: {data.FrameCount}");
                        
                        col.Item().PaddingTop(10);
                        col.Item().Text("Pressure Statistics").SemiBold().FontSize(14);
                        col.Item().Text($"Highest Pressure Found: {data.MaxPressure}");
                        col.Item().Text($"Lowest Pressure Found: {data.MinPressure}");
                        col.Item().Text($"Max Peak Pressure Index: {data.MaxPeakPressure}");
                        col.Item().Text($"Average Pressure: {data.OverallAverage:F2}");
                        col.Item().Text($"Average Contact Area: {data.AverageContactArea:F2}%");
                        
                        col.Item().PaddingTop(10);
                        col.Item().Text($"High Pressure Regions (Top {topRegions.Count} Average Cells)").SemiBold().FontSize(14);
                        
                        foreach (var (region, idx) in topRegions.Select((r, i) => (r, i + 1)))
                        {
                            col.Item().Text($"{idx}. Row: {region.r}, Column: {region.c} - Average Pressure: {region.avg:F2}");
                        }
                });

                page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                    });
            });
        });

        var pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", $"Report_{dateOnly:yyyyMMdd}.pdf");
    }

    private class ReportData
    {
        public int PatientId { get; set; }
        public List<int> MapList { get; set; }
        public DateOnly Day { get; set; }
        public int FrameCount { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int MaxPressure { get; set; }
        public int MinPressure { get; set; }
        public int MaxPeakPressure { get; set; }
        public double OverallAverage { get; set; }
        public double AverageContactArea { get; set; }
    }
}

