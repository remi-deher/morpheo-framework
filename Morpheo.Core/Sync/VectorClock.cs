using System.Text.Json;

namespace Morpheo.Core.Sync;

public enum VectorRelation
{
    Equal,      // Versions identiques
    CausedBy,   // Mon vecteur est plus vieux (je dois me mettre à jour)
    Causes,     // Mon vecteur est plus récent (je domine)
    Concurrent  // CONFLIT : Evenement concurrent
}

public class VectorClock : Dictionary<string, long>
{
    public VectorClock() { }

    public VectorClock(IDictionary<string, long> other) : base(other) { }

    
    /// Incrémente le compteur logique pour ce nœud (Moi).
    
    public void Increment(string nodeId)
    {
        if (!ContainsKey(nodeId))
            this[nodeId] = 0;
        this[nodeId]++;
    }

    
    /// Fusionne avec un vecteur reçu (prend le MAX de chaque case).
    /// Utile après une réconciliation.
    
    public void Merge(IDictionary<string, long> other)
    {
        foreach (var pair in other)
        {
            if (!ContainsKey(pair.Key))
                this[pair.Key] = pair.Value;
            else
                this[pair.Key] = Math.Max(this[pair.Key], pair.Value);
        }
    }

    
    /// Compare "Moi" (this) avec un "Autre" (other).
    /// Retourne la relation temporelle entre les deux.
    
    public VectorRelation CompareTo(IDictionary<string, long> other)
    {
        bool hasGreater = false;
        bool hasLess = false;

        var allKeys = this.Keys.Union(other.Keys);

        foreach (var key in allKeys)
        {
            long myVal = this.TryGetValue(key, out var v1) ? v1 : 0;
            long otherVal = other.TryGetValue(key, out var v2) ? v2 : 0;

            if (myVal > otherVal) hasGreater = true;
            if (myVal < otherVal) hasLess = true;
        }

        if (!hasGreater && !hasLess) return VectorRelation.Equal;
        if (hasGreater && !hasLess) return VectorRelation.Causes;   // Je suis l'ancêtre (je suis plus complet)
        if (!hasGreater && hasLess) return VectorRelation.CausedBy; // Je suis le descendant (je suis en retard)

        return VectorRelation.Concurrent; // Conflit
    }

    // Helpers pour la BDD (Sérialisation)
    public string ToJson() => JsonSerializer.Serialize(this);
    public static VectorClock FromJson(string json)
        => string.IsNullOrEmpty(json) ? new VectorClock() : JsonSerializer.Deserialize<VectorClock>(json) ?? new VectorClock();
}