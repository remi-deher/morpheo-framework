using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions; // Pour TryAddSingleton
using Morpheo.Abstractions;
using Morpheo.Core.Client;
using Morpheo.Core.Data;
using Morpheo.Core.Discovery;
using Morpheo.Core.Printers;
using Morpheo.Core.Server;
using Morpheo.Core.Sync;
using System.Runtime.Versioning;

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo<TDbContext>(
        this IServiceCollection services,
        Action<MorpheoOptions> configure)
        where TDbContext : MorpheoDbContext
    {
        // 1. Configuration des Options
        var options = new MorpheoOptions();
        configure(options);
        options.Validate();
        services.AddSingleton(options);

        // 2. Services Core (Infrastructure)
        services.AddSingleton<MorpheoNode>();
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();
        services.AddSingleton<MorpheoWebServer>();
        services.AddSingleton<DatabaseInitializer>();

        // 3. Moteur de Synchronisation Agnostique (Nouveautés)

        // A. Résolution de Types : Permet de mapper dynamiquement "Product" -> typeof(Product)
        // On l'instancie ici pour pouvoir l'injecter sous deux formes :
        var typeResolver = new SimpleTypeResolver();
        services.AddSingleton<IEntityTypeResolver>(typeResolver); // Pour le moteur interne (Interface)
        services.AddSingleton(typeResolver);                      // Pour l'app utilisateur (Classe concrète pour Register<T>)

        // B. Moteur de Conflit : Décide entre CRDT et Last-Write-Wins
        services.AddSingleton<ConflictResolutionEngine>();

        // C. Service de Synchro (Consomme le moteur de conflit)
        services.AddSingleton<DataSyncService>();

        // D. Maintenance : Service d'arrière-plan pour nettoyer les vieux logs (Garbage Collector)
        services.AddHostedService<LogCompactionService>();

        // 4. Base de Données (SQLite)
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // Permet d'injecter MorpheoDbContext générique même si l'app utilise AppDbContext
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        // 5. IMPRESSION : Pattern "Strategy" avec Fallback
        // Par défaut, on met le "NullGateway" pour éviter les crashs si aucun driver n'est dispo.
        services.TryAddSingleton<IPrintGateway, NullPrintGateway>();

        return services;
    }

    /// Active le support de l'impression via les API Windows (System.Drawing.Printing).
    /// À n'utiliser QUE sur Windows (Opt-in).
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPrinting(this IServiceCollection services)
    {
        // On remplace l'implémentation par défaut par celle de Windows
        services.Replace(ServiceDescriptor.Singleton<IPrintGateway, WindowsPrinterService>());
        return services;
    }
}