using Microsoft.AspNetCore.Mvc.Rendering;
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

    [NotMapped]
    public List<Patient> Patients { get; set; } = [];

    [NotMapped]
    public List<User> AllUsers { get; set; }

    [NotMapped]
    public List<User> AllPatientUsers { get; set; }

    [NotMapped]
    public List<SelectListItem> GetUserItems => AllPatientUsers?.Select(x => new SelectListItem(x.Username, x.Id.ToString()))?.ToList() ?? new List<SelectListItem>();

    [NotMapped]
    public SelectListItem SelectedUserItem { get; set; }
}

