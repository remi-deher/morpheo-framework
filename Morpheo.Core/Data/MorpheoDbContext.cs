using Microsoft.EntityFrameworkCore;
using Morpheo.Abstractions;

namespace Morpheo.Core.Data;

public class MorpheoDbContext : DbContext
{
    private readonly MorpheoOptions _options;

    // Table interne du Framework (ex: File d'attente d'impression, Logs de sync)
    // Le développeur n'a pas à s'en soucier, c'est géré par nous.
    public DbSet<SyncLog> SyncLogs { get; set; }

    // Constructeur standard pour l'injection de dépendance
    public MorpheoDbContext(DbContextOptions options) : base(options)
    {
    }

    // Constructeur permettant de passer les options Morpheo manuellement si besoin
    protected MorpheoDbContext(DbContextOptions options, MorpheoOptions morpheoOptions) : base(options)
    {
        _options = morpheoOptions;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuration des tables internes
        modelBuilder.Entity<SyncLog>().HasKey(x => x.Id);
    }
}

// Exemple d'une table interne technique pour Morpheo
public class SyncLog : MorpheoEntity
{
    public string Action { get; set; } // "UPDATE", "DELETE"
    public string EntityName { get; set; }
    public string SyncedWithNodeId { get; set; }
}