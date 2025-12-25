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

    // Par défaut : 3 secondes. Mais modifiable.
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(3);

    // Par défaut : false (HTTP). Si true -> HTTPS.
    public bool UseSecureConnection { get; set; } = false;

    public void Validate()
    {
        if (DiscoveryPort < 1 || DiscoveryPort > 65535)
            throw new ArgumentException("Le port Morpheo doit être compris entre 1 et 65535.");
    }
}