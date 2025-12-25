using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Morpheo.Abstractions;

namespace Morpheo.Extensions.Security;

/// <summary>
/// Authentificateur "Opt-in" sécurisé.
/// Vérifie que la requête est signée avec une clé secrète partagée (Cluster Secret).
/// Respecte le RFC Morpheo Section 6.
/// </summary>
public class PskHmacAuthenticator : IRequestAuthenticator
{
    private readonly byte[] _secretKey;
    private const string HEADER_NAME = "X-Morpheo-Signature";

    public PskHmacAuthenticator(string preSharedKey)
    {
        _secretKey = Encoding.UTF8.GetBytes(preSharedKey);
    }

    public async Task<bool> IsAuthorizedAsync(HttpContext context)
    {
        // 1. Header présent ?
        if (!context.Request.Headers.TryGetValue(HEADER_NAME, out var receivedSignature))
        {
            return false;
        }

        // 2. IMPORTANT : Autoriser la relecture du Body
        // Sinon le Controller API ne pourra plus lire le JSON après nous.
        context.Request.EnableBuffering();

        // 3. Lire le contenu
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var bodyContent = await reader.ReadToEndAsync();

        // Rembobiner le stream pour la suite du pipeline
        context.Request.Body.Position = 0;

        // 4. Calcul du Hash
        using var hmac = new HMACSHA256(_secretKey);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(bodyContent));
        var computedSignature = Convert.ToHexString(computedHash);

        // 5. Comparaison (Case Insensitive)
        return string.Equals(computedSignature, receivedSignature, StringComparison.OrdinalIgnoreCase);
    }
}