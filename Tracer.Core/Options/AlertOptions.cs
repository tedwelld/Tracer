namespace Tracer.Core.Options;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    public bool CreateAlertsForUnknownDevices { get; set; } = true;
    public int ReturnAlertThresholdMinutes { get; set; } = 120;
}
