using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Core.Entities;
using Tracer.Core.Enums;
using Tracer.Core.Options;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Infrastructure.Services;

public sealed class AlertNotificationService(
    IDbContextFactory<TracerDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<AlertNotificationService> logger)
{
    private readonly NotificationOptions _options = notificationOptions.Value;

    public async Task NotifyAsync(IReadOnlyCollection<long> alertIds, CancellationToken cancellationToken)
    {
        if (alertIds.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var alerts = await dbContext.DeviceAlerts
            .Include(x => x.Device)
            .Where(x => alertIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var emailEnabled = _options.Email.Enabled && !string.IsNullOrWhiteSpace(_options.Email.Host) && _options.Email.ToAddresses.Count > 0;
        var webhookEnabled = _options.Webhook.Enabled && Uri.TryCreate(_options.Webhook.Url, UriKind.Absolute, out _);

        foreach (var alert in alerts)
        {
            alert.NotificationAttempts += 1;
            alert.LastNotificationAttemptUtc = DateTimeOffset.UtcNow;

            if (!emailEnabled && !webhookEnabled)
            {
                alert.NotificationStatus = AlertNotificationStatus.Disabled;
                alert.LastNotificationError = "No notification channel is configured.";
                continue;
            }

            try
            {
                if (webhookEnabled)
                {
                    await SendWebhookAsync(alert, cancellationToken);
                }

                if (emailEnabled)
                {
                    await SendEmailAsync(alert);
                }

                alert.NotificationStatus = AlertNotificationStatus.Sent;
                alert.NotificationSentUtc = DateTimeOffset.UtcNow;
                alert.LastNotificationError = null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispatch alert {AlertId}.", alert.Id);
                alert.NotificationStatus = AlertNotificationStatus.Failed;
                alert.LastNotificationError = ex.Message;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SendWebhookAsync(DeviceAlert alert, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(AlertNotificationService));
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Webhook.Url)
        {
            Content = JsonContent.Create(new
            {
                alert.Id,
                alert.Title,
                alert.Message,
                Severity = alert.Severity.ToString(),
                alert.CreatedUtc,
                Device = new
                {
                    alert.DeviceId,
                    Label = alert.Device?.DisplayName ?? alert.Device?.NetworkName ?? alert.Device?.HardwareAddress ?? alert.Device?.DeviceKey,
                    RadioKind = alert.Device?.RadioKind.ToString(),
                    alert.Device?.VendorName,
                    alert.Device?.RiskScore
                }
            })
        };

        if (!string.IsNullOrWhiteSpace(_options.Webhook.SharedSecret))
        {
            request.Headers.Add("X-Tracer-Signature", _options.Webhook.SharedSecret);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task SendEmailAsync(DeviceAlert alert)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.Email.SenderAddress, _options.Email.SenderDisplayName),
            Subject = $"[{alert.Severity}] {alert.Title}",
            Body = BuildEmailBody(alert),
            IsBodyHtml = false
        };

        foreach (var recipient in _options.Email.ToAddresses.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            message.To.Add(recipient);
        }

        using var smtpClient = new SmtpClient(_options.Email.Host, _options.Email.Port)
        {
            EnableSsl = _options.Email.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.Email.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Email.UserName, _options.Email.Password)
        };

        await smtpClient.SendMailAsync(message);
    }

    private static string BuildEmailBody(DeviceAlert alert)
    {
        var deviceLabel = alert.Device?.DisplayName ?? alert.Device?.NetworkName ?? alert.Device?.HardwareAddress ?? alert.Device?.DeviceKey ?? "Unknown";
        return $"""
            Alert: {alert.Title}
            Severity: {alert.Severity}
            Device: {deviceLabel}
            Radio: {alert.Device?.RadioKind}
            Vendor: {alert.Device?.VendorName ?? "Unknown"}
            Risk Score: {alert.Device?.RiskScore}
            Created UTC: {alert.CreatedUtc:O}

            {alert.Message}
            """;
    }
}
