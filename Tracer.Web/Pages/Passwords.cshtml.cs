using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tracer.Core.Enums;
using Tracer.Infrastructure.Persistence;

namespace Tracer.Web.Pages;

public sealed class PasswordsModel(IDbContextFactory<TracerDbContext> dbContextFactory) : PageModel
{
    public IReadOnlyList<WifiPasswordDto> WifiPasswords { get; private set; } = Array.Empty<WifiPasswordDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        WifiPasswords = await dbContext.DiscoveredDevices
            .AsNoTracking()
            .Where(x => x.RadioKind == RadioKind.Wifi && !string.IsNullOrEmpty(x.Password))
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => new WifiPasswordDto(
                x.Id,
                x.NetworkName ?? x.DeviceKey,
                x.HardwareAddress ?? "Unknown",
                x.SecurityType ?? "Open",
                x.Password!,
                x.DisplayName,
                x.LastSeenUtc,
                x.TotalObservations))
            .ToListAsync(cancellationToken);
    }

    public sealed record WifiPasswordDto(
        Guid Id,
        string NetworkName,
        string HardwareAddress,
        string SecurityType,
        string Password,
        string? DisplayName,
        DateTimeOffset LastSeenUtc,
        int TotalObservations);
}
