using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Morpheo.Abstractions;
using Morpheo.Core.Client; // Pour MorpheoHttpClient
using Morpheo.Core.Data;   // Pour DatabaseInitializer
using Morpheo.Core.Discovery; // Pour UdpDiscoveryService

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

        // --- 3. Configuration du Client HTTP (Pour envoyer des ordres) ---
        services.AddHttpClient(); // Active IHttpClientFactory
        services.AddSingleton<IMorpheoClient, MorpheoHttpClient>();

        // --- 4. Configuration de la Base de Données (SQLite) ---

        // Service qui calcule le chemin du fichier .db selon l'OS
        services.AddSingleton<DatabaseInitializer>();

        // Configuration du contexte Entity Framework utilisateur (TDbContext)
        services.AddDbContext<TDbContext>((provider, dbOptions) =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            var dbPath = initializer.GetDatabasePath();
            dbOptions.UseSqlite($"Data Source={dbPath}");
        });

        // ASTUCE CRUCIALE : On crée un alias pour que MorpheoNode puisse demander 
        // "MorpheoDbContext" et recevoir l'instance de "TDbContext"
        services.AddScoped<MorpheoDbContext>(provider => provider.GetRequiredService<TDbContext>());

        return services;
    }
}