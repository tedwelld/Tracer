using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Interfaces;
using Tracer.Radio.Windows.Services;

namespace Tracer.Radio.Windows;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTracerWindowsRadioScanning(this IServiceCollection services)
    {
        services.AddSingleton<IRadioScanner, WifiScanner>();
        services.AddSingleton<IRadioScanner, BluetoothScanner>();
        return services;
    }
}
