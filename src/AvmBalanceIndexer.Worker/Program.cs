using AvmBalanceIndexer.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<IndexerWorker>();

var host = builder.Build();
host.Run();
