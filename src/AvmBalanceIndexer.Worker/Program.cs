using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Hosting;
using AvmBalanceIndexer.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAvmLogging(builder.Configuration, "avm-worker");
builder.Services.AddChainStorage(builder.Configuration);
builder.Services.AddAlgorandIndexing(builder.Configuration);
builder.Services.AddOptions<WorkerOptions>().Bind(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton<WorkerHealth>();
builder.Services.AddHostedService<StatusSampler>();
builder.Services.AddAvmTelemetry(builder.Configuration, "avm-worker", [WorkerHealth.MeterName]);
builder.Services.AddHostedService<HealthListener>();
builder.Services.AddOptions<ScanOptions>().Bind(builder.Configuration.GetSection(ScanOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<ScanExecutor>();
builder.Services.AddHostedService<IndexerWorker>();

builder.Build().Run();
