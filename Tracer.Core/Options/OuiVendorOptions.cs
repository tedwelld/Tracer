namespace Tracer.Core.Options;

public sealed class OuiVendorOptions
{
    public const string SectionName = "OuiVendor";

    public bool EnableRemoteRefresh { get; set; } = true;
    public string DownloadUrl { get; set; } = "https://standards-oui.ieee.org/oui/oui.csv";
    public int RefreshIntervalDays { get; set; } = 7;
}
