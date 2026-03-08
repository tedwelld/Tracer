namespace Tracer.Core.Entities;

public sealed class LoginAuditLog
{
    public long Id { get; set; }
    public Guid? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
