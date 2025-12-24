using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Data;

public class DatabaseInitializer
{
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly MorpheoOptions _options;

    public DatabaseInitializer(MorpheoOptions options, ILogger<DatabaseInitializer> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Configure le chemin de la base de données selon l'OS
    /// </summary>
    public string GetDatabasePath()
    {
        // Si le dev a spécifié un chemin, on le respecte
        if (!string.IsNullOrWhiteSpace(_options.LocalStoragePath))
            return Path.Combine(_options.LocalStoragePath, "morpheo.db");

        // Sinon, on détermine le meilleur endroit selon l'OS
        string folder;

        if (Environment.OSVersion.Platform == PlatformID.Unix) // Linux & Android (souvent)
        {
            // Sur Android, System.Environment.SpecialFolder.Personal pointe vers le stockage interne privé
            folder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        }
        else // Windows
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        // On crée le dossier Morpheo s'il n'existe pas
        var path = Path.Combine(folder, "MorpheoData");
        Directory.CreateDirectory(path);

        return Path.Combine(path, "morpheo.db");
    }

    public async Task InitializeAsync(MorpheoDbContext context)
    {
        try
        {
            _logger.LogInformation("🛠 Vérification de la base de données locale...");

            // Cette commande magique crée le fichier .db et les tables s'ils n'existent pas
            // Parfait pour le mode "Zero-Conf"
            await context.Database.EnsureCreatedAsync();

            _logger.LogInformation($"✅ Base de données prête : {context.Database.GetDbConnection().DataSource}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur critique lors de l'initialisation de la BDD");
            throw;
        }
    }
}