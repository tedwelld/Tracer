namespace Tracer.Core.Entities;

public sealed class AdminAuditLog
{
    public long Id { get; set; }
    public Guid? AdminUserId { get; set; }
    public AdminUser? AdminUser { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
