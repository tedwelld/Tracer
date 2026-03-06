using Tracer.Core.Contracts;

namespace Tracer.Core.Interfaces;

public interface IScanCoordinator
{
    Task<ScanCycleSummary> ExecuteAsync(CancellationToken cancellationToken);
}
