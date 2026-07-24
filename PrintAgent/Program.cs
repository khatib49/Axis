using Microsoft.Extensions.Hosting;
using PrintAgent;

// Runs as a plain console app OR as a Windows service (when launched by the SCM).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Axis Print Agent";
});

builder.Services.AddHostedService<PrintWorker>();

var host = builder.Build();
host.Run();
