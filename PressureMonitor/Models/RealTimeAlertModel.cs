namespace PressureMonitor.Models;

public class RealTimeAlertModel
{
    public ContactAreaError ContactArea { get; set; } = new ContactAreaError();
    public PeakPressureError PeakPressure { get; set; } = new PeakPressureError();
    public MinimumPressureError MinimumPressure { get; set; } = new MinimumPressureError();

}

//Enum is used to represent different alert levels
//Temporary until the database is updated to include alert levels for errors
public enum AlertLevel
{
    Normal,
    Warning,
    Critical
}

//This class is logic to show an error to the clinician when the contact area percentage is too low
public class ContactAreaError
{
    public int ContactAreaPercentage { get; set; }
    
    //Compares the contact area percentage to multiple threshold values to determine the alert level
    public AlertLevel AlertLevel
    {
        get
        {
            if (ContactAreaPercentage < 20)
                return AlertLevel.Critical;
            if (ContactAreaPercentage < 30)
                return AlertLevel.Warning;
            //If the contact area percentage is above the threshold values, return normal
            return AlertLevel.Normal;
        }
    }
}    

//This class is logic to show an error to the clinician when peak pressure is too high
public class PeakPressureError
{
    public int PeakPressure { get; set; }
    
    public AlertLevel AlertLevel
    {
        get
        {
            if (PeakPressure > 200)
                return AlertLevel.Critical;
            if (PeakPressure > 175)
                return AlertLevel.Warning;
            return AlertLevel.Normal;
        }
    }
}    

//This class is logic to show an error to the clinician when minimum pressure is too low
public class MinimumPressureError
{
    public int MinValue { get; set; }
    
    public AlertLevel AlertLevel
    {
        get
        {
            if (MinValue < 5)
                return AlertLevel.Critical;
            if (MinValue < 10)
                return AlertLevel.Warning;
            return AlertLevel.Normal;
        }
    }
}    