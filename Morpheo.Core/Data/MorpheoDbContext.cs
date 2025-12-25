using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json; // ⚠️ Important pour JsonSerializer
using Microsoft.EntityFrameworkCore;
using Morpheo.Abstractions; // Pour l'accès aux options si besoin
using Morpheo.Core.Sync;    // Pour VectorClock

namespace Morpheo.Core.Data;

public class SyncLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityId { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string JsonData { get; set; } = "{}";
    public string Action { get; set; } = "UPDATE";
    public long Timestamp { get; set; }
    public bool IsFromRemote { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    // Stockage de l'Horloge Vectorielle

    // C'est ce qui est stocké en BDD (Texte)
    public string VectorClockJson { get; set; } = "{}";

    // C'est ce qu'on utilise dans le code (Non stocké, calculé)
    [NotMapped]
    public VectorClock Vector
    {
        get => VectorClock.FromJson(VectorClockJson);
        set => VectorClockJson = value.ToJson();
    }
}

public class MorpheoDbContext : DbContext
{
    public DbSet<SyncLog> SyncLogs { get; set; }

    // Constructeur vide requis pour certains outils
    public MorpheoDbContext() { }

    public MorpheoDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SyncLog>().HasKey(l => l.Id);

        // Index pour accélérer la recherche par entité
        modelBuilder.Entity<SyncLog>().HasIndex(l => l.EntityId);
    }
}