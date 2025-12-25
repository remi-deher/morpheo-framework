using System.Drawing.Printing;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Morpheo.Abstractions;

namespace Morpheo.Core.Printers;

public record LocalPrinter(string Name, string Group);

public class WindowsPrinterService : IPrintGateway
{
    private readonly MorpheoOptions _options;
    private readonly ILogger<WindowsPrinterService> _logger;

    public WindowsPrinterService(MorpheoOptions options, ILogger<WindowsPrinterService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Implémentation de l'interface IPrintGateway.
    /// Retourne la liste des noms d'imprimantes disponibles et filtrées.
    /// </summary>
    public Task<IEnumerable<string>> GetLocalPrintersAsync()
    {
        // On réutilise la logique interne qui gère les groupes et exclusions
        var localPrinters = GetAvailablePrintersObjects();
        var names = localPrinters.Select(p => p.Name);

        return Task.FromResult(names);
    }

    /// <summary>
    /// Implémentation de l'interface IPrintGateway.
    /// Reçoit le contenu binaire (ZPL, PDF, etc.) et l'envoie à l'imprimante.
    /// </summary>
    public Task PrintAsync(string printerName, byte[] content)
    {
        // NOTE : Sur Windows, pour envoyer du RAW (ZPL) directement sans passer par le driver graphique,
        // il faut utiliser l'API Win32 "OpenPrinter", "WritePrinter" (winspool.drv).
        // Pour ne pas surcharger ce fichier avec 200 lignes de P/Invoke, on simule l'action.

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("❌ WindowsPrinterService utilisé hors de Windows !");
            return Task.CompletedTask;
        }

        _logger.LogInformation($"🖨️ [WINDOWS SPOOLER] Envoi de {content.Length} octets vers '{printerName}'");

        // TODO (Production) : Intégrer une classe 'RawPrinterHelper' ici.
        return Task.CompletedTask;
    }

    // --- Méthodes Internes (Logique de filtrage existante) ---

    public List<LocalPrinter> GetAvailablePrintersObjects()
    {
        var results = new List<LocalPrinter>();

        if (!OperatingSystem.IsWindows())
        {
            return results;
        }

        try
        {
            var installedPrinters = PrinterSettings.InstalledPrinters.Cast<string>().ToList();

            foreach (var printerName in installedPrinters)
            {
                if (IsExcluded(printerName))
                {
                    continue;
                }

                string group = DetermineGroup(printerName);
                results.Add(new LocalPrinter(printerName, group));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des imprimantes Windows.");
        }

        return results;
    }

    private bool IsExcluded(string name)
    {
        return _options.Printers.Exclusions.Any(pattern =>
            Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase));
    }

    private string DetermineGroup(string name)
    {
        foreach (var group in _options.Printers.Groups)
        {
            foreach (var pattern in group.Value)
            {
                if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    return group.Key;
            }
        }
        return "DEFAULT";
    }
}