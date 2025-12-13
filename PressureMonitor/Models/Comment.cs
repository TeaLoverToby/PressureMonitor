namespace PressureMonitor.Models;

public class Comment
{
    public int Id { get; set; }
    public string Text { get; set; }
    public int UserId { get; set; }
    public int? MapId { get; set; }
    public int? ParentId { get; set; }
    public DateTime CreatedAt { get; set; }
}