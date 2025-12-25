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

        // 2. Services Core (Indispensables)
        services.AddSingleton<MorpheoNode>();
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();
        services.AddSingleton<MorpheoWebServer>();
        services.AddSingleton<DataSyncService>();
        services.AddSingleton<DatabaseInitializer>();

        // 3. Base de Données
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        // 4. IMPRESSION : Pattern "Strategy" avec Fallback
        // On utilise TryAddSingleton : si l'utilisateur a déjà enregistré son propre IPrintGateway 
        // (via AddWindowsPrinting par exemple), cette ligne ne fera rien.
        // Sinon, on met le "NullGateway" pour éviter les crashs.
        services.TryAddSingleton<IPrintGateway, NullPrintGateway>();

        return services;
    }

    /// <summary>
    /// Active le support de l'impression via les API Windows (System.Drawing.Printing).
    /// À n'utiliser QUE sur Windows.
    /// </summary>
    public static IServiceCollection AddWindowsPrinting(this IServiceCollection services)
    {
        // On enregistre explicitement le service Windows comme implémentation de l'interface
        services.AddSingleton<IPrintGateway, WindowsPrinterService>();
        // Note : Cela écrasera le NullPrintGateway si AddMorpheo est appelé avant, 
        // ou empêchera son ajout si appelé après (grâce à l'ordre d'injection ou au remplacement).
        // Pour être sûr, on peut utiliser Replace :
        services.Replace(ServiceDescriptor.Singleton<IPrintGateway, WindowsPrinterService>());

        return services;
    }
}