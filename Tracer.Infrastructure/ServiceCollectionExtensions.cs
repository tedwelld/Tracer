using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Interfaces;
using Tracer.Core.Options;
using Tracer.Infrastructure.Persistence;
using Tracer.Infrastructure.Services;

namespace Tracer.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTracerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TracerDb")
            ?? "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=TracerDb;MultipleActiveResultSets=true;TrustServerCertificate=true;";

        services.Configure<ScannerOptions>(configuration.GetSection(ScannerOptions.SectionName));
        services.Configure<AlertOptions>(configuration.GetSection(AlertOptions.SectionName));

        services.AddDbContextFactory<TracerDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddSingleton<RuntimeSettingsService>();
        services.AddSingleton<IRuntimeSettingsService>(sp => sp.GetRequiredService<RuntimeSettingsService>());
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<DeviceIntelligenceService>();
        services.AddSingleton<IScanCoordinator, ScanCoordinator>();

        return services;
    }
}
