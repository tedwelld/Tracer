namespace Tracer.Core.Options;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public EmailNotificationOptions Email { get; set; } = new();
    public WebhookNotificationOptions Webhook { get; set; } = new();

    public sealed class EmailNotificationOptions
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SenderAddress { get; set; } = string.Empty;
        public string SenderDisplayName { get; set; } = "Tracer";
        public List<string> ToAddresses { get; set; } = [];
    }

    public sealed class WebhookNotificationOptions
    {
        public bool Enabled { get; set; }
        public string Url { get; set; } = string.Empty;
        public string SharedSecret { get; set; } = string.Empty;
    }
}
