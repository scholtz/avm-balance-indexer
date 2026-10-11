using AvmBalanceIndexer.Hosting;
using AvmBalanceIndexer.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddChainStorage(builder.Configuration);
builder.Services.AddAlgorandIndexing(builder.Configuration);
builder.Services.AddHostedService<IndexerWorker>();

builder.Build().Run();
