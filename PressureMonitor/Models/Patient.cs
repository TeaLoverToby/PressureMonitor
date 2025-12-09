using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PressureMonitor.Models;

public class Patient
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    [ForeignKey("UserId")]
    public User User { get; set; } = null!;
    
    public DateTime? DateOfBirth { get; set; }
    
    public int? ClinicianId { get; set; }
    
    [ForeignKey("ClinicianId")]
    public Clinician? Clinician { get; set; }
    
    public List<PressureMap> PressureMaps { get; set; } = [];
}
