namespace Morpheo.Abstractions;

public enum NodeRole
{
    StandardClient, // Consomme des données
    Relay,          // Peut servir de relai (Mesh)
    Server          // Source de vérité
}

public class MorpheoOptions
{
    // Valeur par défaut standard (facilement surchargeable)
    public const int DEFAULT_PORT = 5555;

    public string NodeName { get; set; } = Environment.MachineName;

    public NodeRole Role { get; set; } = NodeRole.StandardClient;

    // Le développeur pourra écraser cette valeur
    public int DiscoveryPort { get; set; } = DEFAULT_PORT;

    // Validation basique
    public void Validate()
    {
        if (DiscoveryPort < 1 || DiscoveryPort > 65535)
            throw new ArgumentException("Le port d'écoute pour le service Morpheo doit être compris entre 1 et 65535.");
    }
}