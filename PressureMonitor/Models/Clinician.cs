using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PressureMonitor.Models;

public class Clinician
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public User User { get; set; } = null!;

    [StringLength(100)]
    public string? LicenseNumber { get; set; }
    
}

