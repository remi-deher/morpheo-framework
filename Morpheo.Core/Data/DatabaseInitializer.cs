using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Data;

public class DatabaseInitializer
{
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly MorpheoOptions _options;

    public DatabaseInitializer(ILogger<DatabaseInitializer> logger, MorpheoOptions options)
    {
        _logger = logger;
        _options = options;
    }

    
    /// Calcule le chemin du fichier de base de données spécifique à ce nœud.
    /// Ex: .../MorpheoData/morpheo_CAISSE_01.db
    
    public string GetDatabasePath()
    {
        var folder = GetDataFolder();

        // On nettoie le nom pour éviter les caractères interdits dans les noms de fichiers
        var cleanName = string.Join("_", _options.NodeName.Split(Path.GetInvalidFileNameChars()));

        return Path.Combine(folder, $"morpheo_{cleanName}.db");
    }

    public async Task InitializeAsync(MorpheoDbContext context)
    {
        try
        {
            var path = GetDatabasePath();
            _logger.LogInformation($"🛠 Initialisation BDD : {path}");

            // 1. MIGRATION : On vérifie si on doit récupérer une ancienne base générique
            MigrateLegacyDatabase(path);

            // 2. CRÉATION : Si la base n'existe pas, EF Core la crée avec le schéma à jour
            await context.Database.EnsureCreatedAsync();

            _logger.LogInformation("✅ Base de données prête.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "❌ Erreur critique lors de l'initialisation de la base de données.");
            throw;
        }
    }

    
    /// Tente de renommer l'ancien fichier 'morpheo.db' vers le nouveau format 'morpheo_NOM.db'
    /// pour ne pas perdre les données lors de la mise à jour du framework.
    
    private void MigrateLegacyDatabase(string targetNewPath)
    {
        var folder = GetDataFolder();
        var oldLegacyPath = Path.Combine(folder, "morpheo.db");

        // Scénario : L'ancien fichier existe, mais pas encore le nouveau.
        if (File.Exists(oldLegacyPath) && !File.Exists(targetNewPath))
        {
            try
            {
                _logger.LogWarning("⚠️ Détection d'une ancienne base de données 'morpheo.db'. Migration en cours...");

                // On renomme le fichier (Move = Rename)
                File.Move(oldLegacyPath, targetNewPath);

                _logger.LogInformation($"✨ Migration réussie ! Vos données sont maintenant dans : {Path.GetFileName(targetNewPath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Echec de la migration automatique. Une nouvelle base sera créée.");
            }
        }
    }

    private string GetDataFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorpheoData");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
    }
}