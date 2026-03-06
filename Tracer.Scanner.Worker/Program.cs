using Tracer.Infrastructure;
using Tracer.Radio.Windows;
using Tracer.Scanner.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Tracer Scanner Worker";
});

builder.Services.AddTracerInfrastructure(builder.Configuration);
builder.Services.AddTracerWindowsRadioScanning();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
