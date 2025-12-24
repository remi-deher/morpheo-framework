using Microsoft.EntityFrameworkCore;

namespace Morpheo.Core.Data;

public class MorpheoDbContext : DbContext
{
    public MorpheoDbContext(DbContextOptions options) : base(options) { }

    // Table d'historique de synchronisation
    public DbSet<SyncLog> SyncLogs { get; set; }
}

// L'entité stockée en base
public class SyncLog : MorpheoEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string JsonData { get; set; } = "{}";
    public string Action { get; set; } = "CREATE";

    // Pour la gestion des conflits (Heure logique)
    public long Timestamp { get; set; }

    // Pour éviter de renvoyer à l'envoyeur (Ping-Pong)
    public bool IsFromRemote { get; set; } = false;
}