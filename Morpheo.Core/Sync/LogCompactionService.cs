using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;
using Morpheo.Core.Data;

namespace Morpheo.Core.Sync;

public class LogCompactionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MorpheoOptions _options;
    private readonly ILogger<LogCompactionService> _logger;

    public LogCompactionService(IServiceProvider serviceProvider, MorpheoOptions options, ILogger<LogCompactionService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🧹 Service de Compaction (GC) démarré.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCompactionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la compaction des logs.");
            }

            // Attente avant le prochain cycle
            await Task.Delay(_options.CompactionInterval, stoppingToken);
        }
    }

    private async Task RunCompactionAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MorpheoDbContext>();

        // Calcul de la date limite
        var thresholdTick = DateTime.UtcNow.Subtract(_options.LogRetention).Ticks;

        // Suppression des Logs obsolètes
        var logsDeleted = await db.SyncLogs
            .Where(l => l.Timestamp < thresholdTick)
            .ExecuteDeleteAsync();

        if (logsDeleted > 0)
        {
            _logger.LogInformation($"🧹 Compaction : {logsDeleted} anciens logs supprimés.");
        }
    }
}