using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PressureMonitor.Models;

namespace PressureMonitor.Controllers
{
    public class ErrorController : Controller
    {
        public IActionResult Index()
        {
            // 1. Create empty model
            var model = new RealTimeAlertModel();

            // 2. Connection string to your SQLite DB
            var connectionString = "Data Source=PressureMonitor.db";

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                // 3. SQL command to get most recent row
                var command = connection.CreateCommand();
                command.CommandText =
                    @"
                    SELECT 
                        ContactAreaPercentage,
                        PeakPressure,
                        MinValue
                    FROM pressureFrames
                    ORDER BY Timestamp DESC
                    LIMIT 1;
                ";

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        model.ContactArea.ContactAreaPercentage = reader.GetInt32(0);
                        model.PeakPressure.PeakPressure = reader.GetInt32(1);
                        model.MinimumPressure.MinValue = reader.GetInt32(2);
                    }
                }
            }

            return View(model);
        }
    }
}