using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Configuration;
using System.Data.SQLite;

namespace PressureMonitor.Models;

public class Admin
{
    const int users_Id = 0;
    const int users_Username = 1;
    const int users_Email = 2;
    const int users_Password = 3;
    const int users_UserType = 4;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null;

    [NotMapped]
    public List<User> AllUsers { get; set; }

    [NotMapped]
    public List<SelectListItem> GetUserItems => AllUsers?.Select(x => new SelectListItem(x.Username, x.Id.ToString()))?.ToList()?? new List<SelectListItem>();

    [NotMapped]
    public SelectListItem SelectedUserItem { get; set; }
}
