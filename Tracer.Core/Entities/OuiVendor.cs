namespace Tracer.Core.Entities;

public sealed class OuiVendor
{
    public string Prefix { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? Country { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
