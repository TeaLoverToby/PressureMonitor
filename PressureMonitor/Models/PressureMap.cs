using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace PressureMonitor.Models;

// <summary>
// Represents a pressure map session for a patient on a specific day.
public class PressureMap
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int PatientId { get; set; }

    [ForeignKey("PatientId")]
    public Patient Patient { get; set; } = null!;
    public DateOnly Day { get; set; }
    // Collection of pressure frames for this session
    public List<PressureFrame> Frames { get; set; } = [];
    
    // Calculate the average pressure map across all frames
    public int[][] GetAveragePressureMap()
    {
        var averageMap = new int[32][];
        for (var i = 0; i < 32; i++)
        {
            averageMap[i] = new int[32];
        }

        if (Frames.Count == 0)
        {
            return averageMap;
        }

        // Sum up all frames
        foreach (var frame in Frames)
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

        // Divide each cell by the number of frames to get the average
        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
            {
                averageMap[i][j] /= Frames.Count;
            }
        }

        return averageMap;
    }

}

// <summary>
// Represents a single frame of pressure data within a pressure map session.
// </summary>
public class PressureFrame
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    // Foreign key to PressureMap
    public int PressureMapId { get; set; }
    [ForeignKey("PressureMapId")]
    public PressureMap PressureMap { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    // Raw JSON string representing the 32x32 pressure data matrix, stored in the database.
    public string DataJson { get; set; } = "[]";
    // Avereage value of all pixels in the frame.
    public int AveragePressure { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
    // Peak Pressure Index: Highest recorded pressure in the frame, excluding any areas of less than 10 pixels
    public int PeakPressure { get; set; }
    /// <summary>
    // Contact Area %: Percentage of pixels above lower threshold
    /// </summary>
    public double ContactAreaPercentage { get; set; }
    /// <summary>
    /// Provides access to the 32x32 pressure matrix. If set, calculates and updates metrics.
    /// </summary>
    [NotMapped]
    public int[][] Data
    {
        get => JsonSerializer.Deserialize<int[][]>(DataJson) ?? new int[32][];
        set
        {
            DataJson = JsonSerializer.Serialize(value);
            // Only calculated once to optimize performance
            CalculateMetrics(value);
        }
    }
    
    // Internal helper to compute frame statistics
    private void CalculateMetrics(int[][] data)
    {
        var sum = 0;
        var min = 0;
        var max = 0;
        var activePixels = 0;
        const int totalPixels = 32 * 32;
        const int contactThreshold = 15; // Minimum pressure value to count as contact
        
        // Iterate through all cells in matrix
        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
            {
                var value = data[i][j];
                sum += value;
                
                if (value < min) min = value;
                if (value > max) max = value;
                if (value > contactThreshold) activePixels++;
            }
        }

        AveragePressure = sum / totalPixels;
        MinValue = min;
        MaxValue = max;
        ContactAreaPercentage = (activePixels / (double)totalPixels) * 100.0;
        PeakPressure = CalculatePeakPressure(data);
    }
    
    // Identifies the highest pressure point within valid contact regions.
    private int CalculatePeakPressure(int[][] data)
    {
        const int MINIMUM_AREA_SIZE = 10;
        var visited = new bool[32, 32];
        int maxPressure = 0;

        // Iterate through all the cells in the matrix
        for (var i = 0; i < 32; i++)
        {
            for (var j = 0; j < 32; j++)
            {
                // Start a search if we find an unvisited cell with pressure > 0
                if (!visited[i, j] && data[i][j] > 0)
                {
                    var (areaSize, maxInArea) = FloodFillArea(i, j, data, visited);
                    
                    // If it meets the area requirement and has a higher pressure than last recorded, update
                    if (areaSize >= MINIMUM_AREA_SIZE && maxInArea > maxPressure)
                    {
                        maxPressure = maxInArea;
                    }
                }
            }
        }

        return maxPressure;
    }
    
    // Flood Fill Algorithm - used to find connected areas of pressure above threshold
    // Reference: https://www.geeksforgeeks.org/dsa/flood-fill-algorithm/ 
    private (int areaSize, int maxPressure) FloodFillArea(int startX, int startY, int[][] data, bool[,] visited)
    {
        // The stack allows us to track which cells to visit
        var stack = new Stack<(int x, int y)>();
        
        // We start from the initial location and mark it as visited
        stack.Push((startX, startY));
        visited[startX, startY] = true;
        
        int areaSize = 0;
        int  maxPressure = 0;

        // We loop until the required cells are all visited
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            areaSize++;
            
            // Check if it's the highest pressure so far, if so then record it
            if (data[x][y] > maxPressure)
            {
                maxPressure = data[x][y];
            }

            // Now we check the connected neighbours (up, down, left and right)
            var neighbors = new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) };
            
            foreach (var (nx, ny) in neighbors)
            {
                // Check if it's within the bounds of a 32x32 matrix and is above 0 (meaning there is pressure)
                // For performance, we check if it's already visited
                if (nx >= 0 && nx < 32 && ny >= 0 && ny < 32 && !visited[nx, ny] && data[nx][ny] > 0)
                {
                    visited[nx, ny] = true;
                    // Since it's a valid neighbour, we now need to visit it too and check its neighbours (recursion)
                    stack.Push((nx, ny));
                }
            }
        }

        return (areaSize, maxPressure);
    }
}