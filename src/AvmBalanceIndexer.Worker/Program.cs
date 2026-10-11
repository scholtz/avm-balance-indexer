using AvmBalanceIndexer.Hosting;
using AvmBalanceIndexer.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddChainStorage(builder.Configuration);
builder.Services.AddAlgorandIndexing(builder.Configuration);
builder.Services.AddOptions<WorkerOptions>().Bind(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton<WorkerHealth>();
builder.Services.AddHostedService<StatusSampler>();
builder.Services.AddAvmTelemetry(builder.Configuration, "avm-worker", [WorkerHealth.MeterName]);
builder.Services.AddHostedService<HealthListener>();
builder.Services.AddHostedService<IndexerWorker>();

builder.Build().Run();
