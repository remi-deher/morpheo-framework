using Morpheo.Abstractions;
using Microsoft.Extensions.Logging;

namespace Morpheo.Core.Printers;

/// <summary>
/// Implémentation par défaut qui ne fait RIEN.
/// Évite de planter si aucune stratégie d'impression n'est définie (ex: Serveur Linux sans CUPS).
/// </summary>
public class NullPrintGateway : IPrintGateway
{
    private readonly ILogger<NullPrintGateway> _logger;

    public NullPrintGateway(ILogger<NullPrintGateway> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        // On ne retourne rien, proprement.
        return Task.FromResult(Enumerable.Empty<string>());
    }

    public Task PrintAsync(string printerName, byte[] content)
    {
        _logger.LogWarning($"🖨️ IMPRESSION SIMULÉE (NullGateway) : Ordre reçu pour '{printerName}', mais aucun driver n'est configuré.");
        return Task.CompletedTask;
    }
}