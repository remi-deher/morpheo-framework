namespace Morpheo.Abstractions;

public enum NodeRole
{
    StandardClient,
    Relay,
    Server
}

public class MorpheoOptions
{
    public const int DEFAULT_PORT = 5555;

    public string NodeName { get; set; } = Environment.MachineName;
    public NodeRole Role { get; set; } = NodeRole.StandardClient;
    public PrinterOptions Printers { get; } = new();
    public List<string> Capabilities { get; set; } = new();
    public int DiscoveryPort { get; set; } = DEFAULT_PORT;
    public string? LocalStoragePath { get; set; }

    // Configuration réseau
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(3);
    public bool UseSecureConnection { get; set; } = false;

    // --- NOUVEAUTÉS (Pour corriger l'erreur CS1061) ---

    
    /// Durée de conservation des logs de synchro avant nettoyage.
    /// Défaut : 30 jours.
    
    public TimeSpan LogRetention { get; set; } = TimeSpan.FromDays(30);

    
    /// Fréquence de passage du "Garbage Collector" des logs.
    /// Défaut : 1 heure.
    
    public TimeSpan CompactionInterval { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (DiscoveryPort < 1 || DiscoveryPort > 65535)
            throw new ArgumentException("Le port Morpheo doit être compris entre 1 et 65535.");
    }
}