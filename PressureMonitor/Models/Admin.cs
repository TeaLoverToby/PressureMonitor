using DocumentFormat.OpenXml.CustomProperties;
using DocumentFormat.OpenXml.ExtendedProperties;
using Microsoft.Data.Sqlite;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.SQLite;
using System.Configuration;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PressureMonitor.Models;



public class UserInfo
{
    public long Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public long UserType { get; set; }
}

public class Admin
{
    const string connectionString = @"Data Source=PressureMonitor.db";
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
    public User User { get; set; } = null!;

    public List<UserInfo> GetUsers
    {
        get
        {
            List<UserInfo> users = new List<UserInfo>();

            var sql = "SELECT * FROM Users";

            try
            {
                using var connection = new SqliteConnection( WebApplication.CreateBuilder().Configuration.GetConnectionString("DefaultConnection"));
                connection.Open();

                using var command = new SqliteCommand(sql, connection);

                using var reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        UserInfo user = new UserInfo();

                        user.Id = reader.GetInt64(users_Id);
                        user.Username = reader.GetString(users_Username);
                        user.Email = reader.GetString(users_Email);
                        user.Password = reader.GetString(users_Password);
                        user.UserType = reader.GetInt64(users_UserType);

                        users.Add(user);
                    }
                }               
                connection.Close();    
            }
            catch (SqliteException ex)
            {
                Console.WriteLine(ex.Message);
            }

            return users;
        }
    }
    [NotMapped]
    public List<SelectListItem> GetUserItems => GetUsers.Select(x => new SelectListItem(x.Username, x.Id.ToString())).ToList();

}
