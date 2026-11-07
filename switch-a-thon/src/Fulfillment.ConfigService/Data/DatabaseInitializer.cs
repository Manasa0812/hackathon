namespace Fulfillment.ConfigService.Data;

public sealed class DatabaseInitializer(ConfigurationDatabase database, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        logger.LogInformation("Configuration database is ready and demo data is seeded.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
