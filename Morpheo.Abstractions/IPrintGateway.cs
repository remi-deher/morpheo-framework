namespace Morpheo.Abstractions;

public interface IPrintGateway
{
    // Liste les imprimantes installées sur la machine locale
    Task<IEnumerable<string>> GetLocalPrintersAsync();

    // Envoie un ordre d'impression (RAW ZPL, PDF, etc.)
    Task PrintAsync(string printerName, byte[] content);
}