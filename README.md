**🌐 Morpheo.Sdk**

**Le Framework de Résilience Distribuée pour Applications Critiques (.NET Standard)**

**Morpheo.Sdk** n'est pas une simple librairie réseau. C'est un changement de paradigme complet pour les applications métiers (LOB). Il transforme une application client-serveur fragile en un **système distribué, auto-organisé et indestructible**, capable de fonctionner sans serveur, sans internet, et sans configuration informatique complexe.

**Morpheo.Sdk** est prévu pour les bases de données mais aussi pour les **services d'impression distribués** souvent lourd à installer et maintenir

*la mise en place de noeud d'impression est facilité*

-----
**🚀 Comment Morpheo révolutionner les infrastructures logiciels ?**

L'architecture logicielle standard (Client-Serveur) est obsolète pour les environnements physiques instables (usines, magasins, logistique). Voici pourquoi **Morpheo** reconcillie les architrectures applicatives en places :

|**Critère**|**❌ Architecture Standard (Legacy)**|**✅ Architecture Morpheo (Révolutionnaire)**|
| :- | :- | :- |
|**Dépendance**|Si le serveur tombe, tout s'arrête.|**Zéro Single Point of Failure.** Le système survit à la perte de n'importe quel nœud.|
|**Configuration**|Nécessite des IP fixes, DNS, VPN.|**Zero-Conf.** Découverte automatique des voisins via UDP/SSDP.|
|**Données**|Centralisées (une seule vérité).|**Distribuées & Réconciliées.** Chaque nœud possède une vérité locale qui converge globalement.|
|**Matériel**|Une imprimante est liée à un PC.|**Hardware Mesh.** N'importe quel utilisateurs peut utiliser l'imprimante de n'importe quel autre PC.|
|**Flexibilité**|Rôles figés (Client ou Serveur).|**Rôles Liquides.** Un client peut devenir maitre ou serveur temporaire en une milliseconde.|
|**Configuration**|Configuration figés.|**Flexibilité.** Un client peut être configuré de manière chirurgicale pour répondre au besoin de resilience.|

-----

**💎 La Règle d'Or : "Opt-in Complexity"**

Morpheo respecte votre architecture. Il ne force jamais l'utilisation de composants lourds ou spécifiques à un OS.

***Convention over Configuration*** : Par défaut, Morpheo démarre en mode "Zéro Config" : HTTP standard, Discovery optimisé, et moteur d'impression neutre.

***Opt-in Complexity*** : Vous avez besoin de HTTPS ? De l'impression via le Spooler Windows ? D'une stratégie de sécurité fine ? Vous l'activez explicitement.

-----

**🏗 Architecture & Concepts Clés**

Le framework repose sur trois piliers fondamentaux qui abstraient la complexité du réseau pour le développeur.

**1. La Cascade de Données (Data Failover)**

Morpheo ne demande jamais "Où est la source de données ?". Il demande "Quelle est la meilleure source disponible maintenant ?".

Le système applique une stratégie de repli automatique en temps réel :

|**Priorité**|**Source**|**Condition d'activation**|**Usage**|
| :- | :- | :- | :- |
|**1️⃣ Niv. 1**|**Serveur Dédié (Production)**|API / MariaDB joignable|Source de vérité absolue. Synchronisation globale.|
|**1️⃣ Niv. 2**|**Base de donnée structure minimal (Production)**|Base de donnée|Synchronisation globale.|
|**2️⃣ Niv. 3**|**Mesh P2P (Voisinage)**|Serveur HS, Voisins détectés|Échange de données entre pairs. Élection automatique d'un leader.|
|**3️⃣ Niv. 4**|**Local (Survie)**|Réseau totalement coupé|Stockage SQLite local (AppDbContext).|

**Note :** La réconciliation se fait via des GUIDs universels et des Vecteurs de Temps, garantissant qu'aucune donnée n'est écrasée ou perdue lors des transitions entre ces modes.

**2. Le Mesh d'Impression (Hardware Relay)**

Fini les drivers d'imprimante à installer partout. Morpheo peut transformer chaque nœud Windows en un **Serveur d'Impression Potentiel**.

- **Scénario :** Une tablette (Web) envoie un ordre d'impression.
- **Routage :** Le serveur (ou le Mesh) détecte quel PC / Serveurs Windows possède l'imprimante cible (Zebra, Dymo).
- **Exécution :** L'ordre est routé via SignalR/HTTP vers la cible qui imprime localement.
- **Résilience:** Si un noeud n'est pas disponible on distribue l'impression à un autre noeud

**3. La Découverte Dynamique (Discovery)**

Basé sur un protocole UDP Broadcast robuste (inspiré du SSDP), les nœuds crient "Je suis là" et "Voici mes capacités".

- *Pas besoin d'entrer l'IP du serveur.*
- *Pas besoin de configurer les clients.*
- L'ajout d'un nouveau poste se fait en le branchant simplement au réseau.
-----
**⚙️ Topologies Supportées**

Le framework permet de mixer ces configurations au sein d'une même flotte.

**A. Mode Standalone**

L'application fonctionne sur un seul PC. Base de données locale (SQLite). Aucun trafic réseau.

- *Idéal pour : TPE, Postes isolés.*

**B. Mode "Base de données partagés**

Permet à plusieurs PC d'utiliser une base de donnée partagés déja en production (comme MariaDB, Postgress, Oracle,....)

- *Idéal pour : TPE, Equipes mobiles.*

**C. Mode "Mesh" (Peer-to-Peer)**

Plusieurs PC se découvrent. Ils synchronisent leurs bases locales entre eux. Si un PC s'éteint, les autres continuent.

- *Idéal pour : Équipes mobiles, Chantiers temporaires, Panne serveur.*

**D. Mode "Hybride & HA" (Production)**

Un serveur dédié (Docker/Linux / Windows ) centralise les données. Les clients s'y connectent en priorité. Si le serveur tombe, ils basculent instantanément en mode "Essaim" sans interruption de service pour l'utilisateur.

- *Idéal pour : Usines, Logistique, Grande Distribution.*
-----
**🛠 Guide d'Intégration Rapide**

Voici comment Morpheo gère la complexité pour vous.

**Initialisation du Nœud (C#)**

```
var node = new MorpheoNode(new NodeConfiguration 

{

`    `Role = NodeRole.StandardClient, // ou Relay, ou Server

`    `Capabilities = NodeCapabilities.CanPrint | NodeCapabilities.HasDatabase,

`    `FailoverStrategy = FailoverStrategy.Cascade // Tentative Server -> P2P -> Local

});

node.StartDiscovery(); // Lance l'écoute UDP
```
**Envoi d'une Donnée (Agnostique) (C#)**

Le développeur ne se soucie pas de la destination. Le framework route intelligemment.

```
// Le framework décide si ça part au serveur, au voisin, ou en local

await node.DataLayer.SaveAsync(new ProductLabel { ... }); 

**Requête d'Impression Distribuée**
```

```
// Demande d'impression sur l'imprimante "ZEBRA\_ACCUEIL"

// Le framework trouve quel PC possède cette imprimante et lui transmet l'ordre.

await node.PrintLayer.RemotePrintAsync("ZEBRA\_ACCUEIL", labelData);
```
-----
**📊 Matrice de Résilience**

Comment le système réagit-il aux catastrophes ?

|**Incident**|**Comportement Système**|**Impact Utilisateur**|
| :- | :- | :- |
|**Coupure Internet**|Bascule en **Mesh P2P**. Synchronisation locale maintenue.|**Aucun.** (Indicateur "Mode Local" affiché).|
|**Crash Serveur Dédié**|Utilisation d'une base de donnée|**Aucun.** Les données sont mises en cache.|
|**Panne Base de donnée**|Les autres PC continuent en mesh.|**Aucun.**|
|**Coupure de réseau sur un poste**|Le PC passe sur sa base de donéne locale (SqLite).|**Aucun.**|
|**Coupure Courant Générale**|Redémarrage. Réconciliation des données locales au retour du courant.|**Aucun.** (Pas de perte de données grâce au cache disque).|
|**Retour du serveur ou BDD**|**"Healing Process".** Le Mesh pousse les données tampon.|**Transparent.**|

-----
**🔮 Roadmap & Évolution**

Ce framework est né du besoin d'avoir un système résilient

- [x] **Core :** Découverte UDP & Gestion BDD Locale (SQLite).
- [x] **Relay :** Serveur HTTP embarqué pour réception d'ordres.
- [ ] **Sync Engine :** Algorithme de Vecteurs de Temps pour la réconciliation P2P.
- [ ] **Cups** : Intégration avec le service d'impression Cups
- [ ] **Security :** Chiffrement des échanges P2P (TLS/Handshake).
- [ ] **Web Admin :** Dashboard de visualisation de la topologie du Mesh.
-----

*Propulsé par .NET 10
