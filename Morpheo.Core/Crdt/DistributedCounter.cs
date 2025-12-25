using System.Text.Json.Serialization;

namespace Morpheo.Core.Crdt;


/// Implémentation générique d'un Pn-Counter (Positive-Negative Counter).
/// Permet de gérer des quantités (Stocks, Solde) sans conflit.

public class DistributedCounter
{
    // P : Incréments (Entrées) par Nœud
    [JsonInclude]
    public Dictionary<string, int> P { get; set; } = new();

    // N : Décréments (Sorties) par Nœud
    [JsonInclude]
    public Dictionary<string, int> N { get; set; } = new();

    // Valeur calculée : Somme(P) - Somme(N)
    [JsonIgnore]
    public int Value => P.Values.Sum() - N.Values.Sum();

    public void Increment(string nodeId, int amount = 1)
    {
        if (!P.ContainsKey(nodeId)) P[nodeId] = 0;
        P[nodeId] += amount;
    }

    public void Decrement(string nodeId, int amount = 1)
    {
        if (!N.ContainsKey(nodeId)) N[nodeId] = 0;
        N[nodeId] += amount;
    }

    
    /// Fonction de fusion mathématique (Commutative, Associative, Idempotente).
    /// P_final = Max(P_local, P_remote)
    
    public static DistributedCounter Merge(DistributedCounter local, DistributedCounter remote)
    {
        var merged = new DistributedCounter();
        MergeDict(merged.P, local.P, remote.P);
        MergeDict(merged.N, local.N, remote.N);
        return merged;
    }

    private static void MergeDict(Dictionary<string, int> target, Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var keys = a.Keys.Union(b.Keys);
        foreach (var key in keys)
        {
            int valA = a.TryGetValue(key, out int vA) ? vA : 0;
            int valB = b.TryGetValue(key, out int vB) ? vB : 0;
            target[key] = Math.Max(valA, valB);
        }
    }
}