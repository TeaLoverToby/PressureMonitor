using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PressureMonitor.Models;

public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    // The string length is specified to prevent long names + memory issues
    [StringLength(50)]
    public string? Username { get; set; }
    
    [StringLength(100)]
    public string? Email { get; set; }
    
    [StringLength(255)]
    public string? Password { get; set; }
    
    public UserType UserType { get; set; } = UserType.Patient;
    
    // This is nullable data for certain user types
    public Clinician? Clinician { get; set; }
    public Patient? Patient { get; set; }
    
    public Admin? Admin { get; set; }
}