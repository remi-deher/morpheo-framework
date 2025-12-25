using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core.Client;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo<TDbContext>(
        this IServiceCollection services,
        Action<MorpheoOptions> configure)
        where TDbContext : MorpheoDbContext
    {
        // 1. Configuration
        var options = new MorpheoOptions();
        configure(options);
        options.Validate();
        services.AddSingleton(options);

        // 2. Services Core
        services.AddSingleton<MorpheoNode>();
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();

        // 3. Client & Serveur
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();

        // IMPORTANT : On enregistre le WebServer pour qu'il soit injectable si besoin
        services.AddSingleton<MorpheoWebServer>();

        // 4. Imprimantes
        services.AddSingleton<WindowsPrinterService>();

        // 5. Synchro
        services.AddSingleton<DataSyncService>();

        // 6. Base de Données
        services.AddSingleton<DatabaseInitializer>();

        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // Alias pour MorpheoDbContext
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        return services;
    }
}