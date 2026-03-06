using Tracer.Core.Enums;

namespace Tracer.Core.Entities;

public sealed class ScanEventLog
{
    public long Id { get; set; }
    public Guid? ScanBatchId { get; set; }
    public ScanBatch? ScanBatch { get; set; }
    public Guid? DeviceId { get; set; }
    public DiscoveredDevice? Device { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ScannerNode { get; set; } = string.Empty;
    public string? RadioKind { get; set; }
    public ScanEventLevel Level { get; set; }
    public ScanEventType EventType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
