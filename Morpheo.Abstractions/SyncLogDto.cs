using System.Collections.Generic;

namespace Morpheo.Abstractions;

public record SyncLogDto(
    string Id,
    string EntityId,
    string EntityName,
    string JsonData,
    string Action,
    long Timestamp,
    Dictionary<string, long> VectorClock, // Horloge vectorielle
    string OriginNodeId                   // ID du nœud d'origine
); //