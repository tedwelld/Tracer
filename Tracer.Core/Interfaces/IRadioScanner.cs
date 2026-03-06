using Tracer.Core.Contracts;
using Tracer.Core.Enums;

namespace Tracer.Core.Interfaces;

public interface IRadioScanner
{
    RadioKind RadioKind { get; }
    Task<IReadOnlyCollection<RadioDeviceSnapshot>> ScanAsync(CancellationToken cancellationToken);
}
