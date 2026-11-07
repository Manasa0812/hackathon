using ModelContextProtocol.Server;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResolveIQ.InvestigatorMcp;
using Npgsql;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var databaseConnectionString = builder.Configuration["ConnectionStrings:ReadOnlyDatabase"]
    ?? "Host=localhost;Port=5432;Database=resolveiq;Username=resolveiq_reader;Password=resolveiq_reader_dev;Timeout=3;Command Timeout=5";
var redisConfiguration = builder.Configuration["Redis:Configuration"]
    ?? "localhost:6379,abortConnect=false,connectTimeout=2000,syncTimeout=3000";

builder.Services.AddHttpClient();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(databaseConnectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfiguration));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<InvestigationTools>();

await builder.Build().RunAsync();
