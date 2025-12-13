namespace PressureMonitor.Models
{
   public static class RealTimeAlertEvaluator
   {
       // This evaluator is used with predefined thresholds to evaluate a PressureFrame which would've been added to a real time alert
       // Evaluate a frame and return a small model describing levels per metric.
       public static RealTimeAlertModel Evaluate(PressureFrame frame)
       {
           const int PeakWarning = 150;
           const int PeakCritical = 200;


           const int ContactAreaWarning = 20;   // lower is worse
           const int ContactAreaCritical = 10;


           const int MinPressureWarning = 10;   // lower is worse
           const int MinPressureCritical = 5;


           //Series of comparison statements to determine the alert level for each metric.
           AlertLevel EvaluateHigherIsWorse(double value, int warning, int critical)
           {
               if (value >= critical) return AlertLevel.Critical;
               if (value >= warning) return AlertLevel.Warning;
               return AlertLevel.None;
           }


           AlertLevel EvaluateLowerIsWorse(double value, int warning, int critical)
           {
               if (value <= critical) return AlertLevel.Critical;
               if (value <= warning) return AlertLevel.Warning;
               return AlertLevel.None;
           }


           return new RealTimeAlertModel
           {
               PeakPressureLevel = EvaluateHigherIsWorse(frame.PeakPressure, PeakWarning, PeakCritical),
               ContactAreaLevel = EvaluateLowerIsWorse(frame.ContactAreaPercentage, ContactAreaWarning, ContactAreaCritical),
               MinimumPressureLevel = EvaluateLowerIsWorse(frame.MinValue, MinPressureWarning, MinPressureCritical)
           };
       }
   }


   //This was originally a short term measure to assign alert levels before the db was fully integrated.
   public enum AlertLevel
   {
       None = 0,
       Warning = 1,
       Critical = 2
   }


   public class RealTimeAlertModel
   {
       public AlertLevel ContactAreaLevel { get; set; }
       public AlertLevel PeakPressureLevel { get; set; }
       public AlertLevel MinimumPressureLevel { get; set; }
   }
}
