using Tracer.Core.Contracts;

namespace Tracer.Core.Interfaces;

public interface IRuntimeSettingsService
{
    Task<RuntimeSettingsSnapshot> GetCurrentAsync(CancellationToken cancellationToken);
    Task<RuntimeSettingsSnapshot> UpdateAsync(RuntimeSettingsSnapshot snapshot, CancellationToken cancellationToken);
}
