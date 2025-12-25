using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;
using Morpheo.Abstractions;
using Morpheo.Core.Client;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using Morpheo.Core.Sync.Strategies; // <--- Nécessaire pour MeshBroadcastStrategy

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

        // 2. Services Core Infrastructure
        services.AddSingleton<MorpheoNode>();
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();
        services.AddSingleton<MorpheoWebServer>();
        services.AddSingleton<DatabaseInitializer>();

        // 3. Moteur de Synchronisation (Agnostique & Opt-in)

        // A. Résolution de Types (Mappage Nom <-> Type C#)
        var typeResolver = new SimpleTypeResolver();
        services.AddSingleton<IEntityTypeResolver>(typeResolver);
        services.AddSingleton(typeResolver);

        // B. Moteur de Conflit (CRDT vs LWW)
        services.AddSingleton<ConflictResolutionEngine>();

        // C. Stratégie de Routage (Failover)
        // Par défaut (Convention) : On diffuse à tout le monde (Mesh).
        // L'utilisateur peut remplacer cela par une stratégie "ServerFirst" via services.AddSingleton<ISyncRoutingStrategy>(...) APRES AddMorpheo.
        services.TryAddSingleton<ISyncRoutingStrategy, MeshBroadcastStrategy>();

        // D. Service Principal de Synchro
        services.AddSingleton<DataSyncService>();

        // E. Maintenance (Garbage Collector des Logs)
        services.AddHostedService<LogCompactionService>();

        // 4. Base de Données
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        // 5. Impression (Hardware Abstraction)
        services.TryAddSingleton<IPrintGateway, NullPrintGateway>();

        return services;
    }

    /// <summary>
    /// Active le support de l'impression via les API Windows.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPrinting(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IPrintGateway, WindowsPrinterService>());
        return services;
    }
}