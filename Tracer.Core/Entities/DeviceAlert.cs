using Tracer.Core.Enums;

namespace Tracer.Core.Entities;

public sealed class DeviceAlert
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public DiscoveredDevice? Device { get; set; }
    public AlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Pending;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? AcknowledgedUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
