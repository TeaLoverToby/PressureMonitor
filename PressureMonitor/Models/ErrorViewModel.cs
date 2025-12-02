namespace PressureMonitor.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    
}

//This class is logic to show an error to the clincician when the contact area percentage is too low
public class ContactAreaError
{
    //Declares private variable to not have any effect on the database
    private int actualArea;
    set
    {
        actualArea = ContactAreaPercentage;
    }
    //Compares the contact area percentage to a minimum threshold value
    if (actualArea < 20)
    {
        throw new ArgumentOutOfRangeException(nameof(ContactAreaPercentage), "Contact area percentage is too low, reseat the patient.");
    }
}    

//This class is logic to show an error to the clincician when the peak pressure is too high
public class PeakPressureError
{
    //Declares private variable to not have any effect on the database
    private int actualPeakPressure;
    set
    {
        actualPeakPressure = PeakPressure;
    }
    //Compares the peak pressure to a maximum threshold value
    if (actualPeakPressure > 200)
    {
        throw new ArgumentOutOfRangeException(nameof(PeakPressure), "Peak pressure is too high, reseat the patient.");
    }
}

//This class is logic to show an error to the clincician when the minimum pressure is too low
public class MinimumPressureError
{
    //Declares private variable to not have any effect on the database
    private int actualMinimumPressure;
    set
    {
        actualMinimumPressure = MinValue;
    }
    //Compares the minimum pressure to a minimum threshold value
    if (actualMinimumPressure < 5)
    {
        throw new ArgumentOutOfRangeException(nameof(MinValue), "Minimum pressure is too low, reseat the patient.");
    }
}