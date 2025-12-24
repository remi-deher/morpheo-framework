using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core.Client;    // Pour MorpheoHttpClient
using Morpheo.Core.Data;      // Pour DatabaseInitializer
using Morpheo.Core.Discovery; // Pour UdpDiscoveryService
using Morpheo.Core.Printers;  // Pour WindowsPrinterService
using Morpheo.Core.Sync;      // Pour DataSyncService

namespace Morpheo.Core;

public static class MorpheoServiceExtensions
{
    public static IServiceCollection AddMorpheo<TDbContext>(
        this IServiceCollection services,
        Action<MorpheoOptions> configure)
        where TDbContext : MorpheoDbContext
    {
        // --- 1. Configuration et Validation des options ---
        var options = new MorpheoOptions();
        configure(options);

        // Sécurité : On vérifie que le port est valide avant d'aller plus loin
        options.Validate();

        services.AddSingleton(options);

        // --- 2. Enregistrement des Services Core ---
        services.AddSingleton<MorpheoNode>();

        // Enregistrement du service de découverte par défaut (UDP Broadcast)
        services.AddSingleton<INetworkDiscovery, UdpDiscoveryService>();

        // --- 3. Client HTTP (Envoi d'ordres & Sync) ---
        services.AddHttpClient();
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();

        // --- 4. Service d'Imprimantes (Printers) ---
        // Permet de scanner les imprimantes Windows locales
        services.AddSingleton<WindowsPrinterService>();

        // --- 5. Service de Synchronisation (Data Sync) ---
        // Moteur de réplication "Last Write Wins"
        services.AddSingleton<DataSyncService>();

        // --- 6. Base de Données (SQLite) ---

        // Service utilitaire pour les chemins de fichiers
        services.AddSingleton<DatabaseInitializer>();

        // Configuration du contexte Entity Framework utilisateur (TDbContext)
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // On crée un alias pour que MorpheoNode (et DataSyncService) 
        // puisse demander "MorpheoDbContext" et recevoir l'instance de "TDbContext"
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        return services;
    }
}