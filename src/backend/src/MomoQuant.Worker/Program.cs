using MomoQuant.Application;
using MomoQuant.Infrastructure;
using MomoQuant.Persistence;
using MomoQuant.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPersistence(builder.Configuration);

builder.Services.AddHostedService<WorkerService>();

var host = builder.Build();

await host.Services.ApplyMigrationsAsync();

host.Run();
