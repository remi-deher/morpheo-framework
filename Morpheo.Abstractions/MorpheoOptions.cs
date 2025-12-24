namespace Morpheo.Abstractions;

// L'énumération des rôles (nécessaire ici)
public enum NodeRole
{
    StandardClient,
    Relay,
    Server
}

public class MorpheoOptions
{
    // Constante par défaut
    public const int DEFAULT_PORT = 5555;

    // Nom du nœud (ex: "PC-CAISSE-01")
    public string NodeName { get; set; } = Environment.MachineName;

    // Rôle du nœud
    public NodeRole Role { get; set; } = NodeRole.StandardClient;

    // Port UDP
    public int DiscoveryPort { get; set; } = DEFAULT_PORT;

    // Chemin de stockage de la BDD (Optionnel, d'où le '?')
    public string? LocalStoragePath { get; set; }

    // Validation de sécurité
    public void Validate()
    {
        if (DiscoveryPort < 1 || DiscoveryPort > 65535)
            throw new ArgumentException("Le port Morpheo doit être compris entre 1 et 65535.");
    }
}