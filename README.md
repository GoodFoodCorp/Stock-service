# Stock Service

Microservice **.NET 8** de gestion des **stocks** et du **réapprovisionnement**
des restaurants franchisés.

| | |
|---|---|
| **Langage / techno** | C# / .NET 8, MediatR (CQRS), EF Core, FluentValidation, Serilog, Swashbuckle |
| **Base de données** | PostgreSQL (port hôte `5434`) |
| **Port HTTP** | `8083` |
| **Documentation API** | http://localhost:8083/docs |

---

## Architecture — Clean Architecture en couches

```
StockService.slnx
src/
├── Core/
│   ├── Stock.Domain/          # Entités (StockItem, StockMovement,
│   │                          # ReplenishmentRequest), règles métier, erreurs
│   │                          # typées, interfaces des repositories
│   │                          # → AUCUNE dépendance externe (pas d'EF Core ici)
│   └── Stock.Application/     # CQRS : un Command/Query + Handler + Validator
│                              # par cas d'usage (FluentValidation via MediatR)
├── Infrastructure/
│   └── Stock.Infrastructure/  # DbContext EF Core, repositories, migrations
└── Presentation/
    └── Stock.Api/             # Contrôleurs fins (une ligne : mediator.Send),
                               # JWT, middleware d'erreurs, Serilog JSON, Swagger
tests/
├── Stock.Domain.Tests/        # xUnit + FluentAssertions
└── Stock.Application.Tests/   # + Moq
```

**Règle** : toute modification de quantité passe par l'agrégat `StockItem`, ce qui
rend impossible de contourner la piste d'audit ou la règle de réapprovisionnement.

---

## Fonctionnalités

### Stocks
- **Lister les articles en stock** de son restaurant
- **Consulter le détail** d'un article avec **l'historique de ses mouvements**
- **Créer un article** (nom, unité, quantité, seuils min et max)
- **Enregistrer un mouvement** : `IN` (réception), `OUT` (sortie/service),
  `ADJUSTMENT` (inventaire, valeur absolue)
- **Piste d'audit complète** : chaque mouvement trace qui, quoi, pourquoi et quand
- Refus des sorties supérieures au stock disponible (`409`)

### Réapprovisionnement
- **Génération automatique** d'une demande quand une sortie fait passer la
  quantité **sous le seuil minimum** — la quantité demandée ramène au seuil
  maximum, et aucun doublon n'est créé si une demande est déjà en attente
- **Création manuelle** d'une demande par le franchisé
- **Suivi des demandes** avec leur statut
- **Changement de statut** réservé au siège (`admin`)
- La **réception** (`RECEIVED`) réintègre automatiquement la quantité en stock

### Cycle de vie d'une demande

```
PENDING → APPROVED → ORDERED → RECEIVED
   └──────────┴──────────┴────▶ CANCELLED
```

### Cloisonnement
Un franchisé ne voit et ne modifie que le stock de **son** restaurant (déduit du
`tenant_id` de son JWT). Le siège peut cibler n'importe quel restaurant.

---

## Endpoints

| Méthode | Route | Accès |
|---|---|---|
| GET | `/api/stocks` | `manager`, `admin` |
| GET | `/api/stocks/{id}` (détail + mouvements) | `manager`, `admin` |
| POST | `/api/stocks` | `manager`, `admin` |
| POST | `/api/stocks/{id}/movements` | `manager`, `admin` |
| GET | `/api/replenishment-requests` | `manager`, `admin` |
| POST | `/api/replenishment-requests` | `manager`, `admin` |
| PATCH | `/api/replenishment-requests/{id}/status` | `admin` (décision siège) |
| GET | `/healthz`, `/readyz` | public (sondes) |

### Endpoints internes (appelés par `order-service`)

Routes sous `/internal/stocks/...`, **jamais** sous `/api/...` (l'ingress ne route
que `/api/...` : elles restent internes au cluster). Elles exigent l'en-tête
`X-Service-Key` égal à `SERVICE_API_KEY` ; sinon `401` (un JWT utilisateur ne suffit pas).

| Méthode | Route | Corps | Réponse 200 |
|---|---|---|---|
| POST | `/internal/stocks/check` | `{tenantId, lines:[{stockItemId, quantity}]}` | `{available, missing:[{stockItemId, name, unit, required, onHand}]}` |
| POST | `/internal/stocks/consume` | `{orderId, tenantId, lines:[…]}` | `{status: APPLIED \| ALREADY_APPLIED \| SKIPPED_CANCELLED, shortfalls:[{stockItemId, shortfall}]}` |
| POST | `/internal/stocks/restore` | `{orderId, tenantId}` | `{status: RESTORED \| ALREADY_RESTORED \| CANCELLED_BEFORE_CONSUME}` |

`consume` est idempotent par `orderId` ; `restore` remet exactement les quantités
réellement retirées (une pénurie partielle n'est pas sur-restituée). Un
mouvement `OUT` issu d'une commande à court d'ingrédient est marqué
« insuffisant » : son champ `shortfall` (exposé dans l'historique des
mouvements, `GET /api/stocks/{id}`) donne la quantité manquante (0 sinon).

---

## Dépendances

> **Légende** — 🔴 indispensable (le service ne démarre pas ou ne sert à rien) ·
> 🟠 nécessaire à une fonctionnalité (le reste continue de marcher) ·
> 🟡 optionnelle (dégradation silencieuse, journalisée)

| Dépendance | Type | Conséquence si absente |
|---|---|---|
| **PostgreSQL** (`stock-db`) | 🔴 | Le service ne démarre pas |
| **auth-service** | 🟠 | Aucun appel réseau, mais toutes les routes `/api` exigent un jeton valide |

**Aucun appel sortant.** `stock-service` est totalement autonome : il ne dépend
d'aucun autre service Good Food au moment de l'exécution.

### Qui dépend de ce service

| Service | Type | Conséquence si `stock-service` est arrêté |
|---|---|---|
| `web-app` | 🟠 | Les pages **Stocks** et **Réapprovisionnement** du portail sont cassées, ainsi que deux indicateurs du tableau de bord. Le reste de l'application fonctionne. |
| `order-service` | 🟠 | Appelle `/internal/stocks/check`, `consume` et `restore` (clé `X-Service-Key`). Les **commandes comportant un plat avec recette sont refusées** (`503`) faute de pouvoir vérifier le stock ; les commandes sans plat suivi passent. La consommation à la confirmation et la remise en stock à l'annulation sont différées puis rejouées par le worker d'`order-service` au retour du service. |

Seuls `web-app` (routes `/api`) et `order-service` (routes `/internal`) l'appellent.

---

## Lancement

```bash
docker network create microservices-net   # une seule fois, partagé
cp .env.example .env                      # renseigner POSTGRES_PASSWORD, JWT_SECRET et SERVICE_API_KEY
docker compose up -d --build
```

⚠️ `JWT_SECRET` doit être **identique** à celui de `auth-service`.
Les migrations EF Core sont appliquées automatiquement au démarrage.

### Variables d'environnement

| Variable | Requis | Description |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | oui | Base dédiée `stock-db` |
| `JWT_SECRET` | oui | Secret HS256 partagé avec `auth-service` |
| `SERVICE_API_KEY` | oui (démarrage refusé sinon) | Clé des appels internes (`X-Service-Key`), identique à celle d'`order-service` |
| `ConnectionStrings__StockDb` | hors Docker | Chaîne de connexion Npgsql |

---

## Tests

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test StockService.slnx   # 67 tests
```

Couvrent les mouvements, la règle de réapprovisionnement automatique, le cycle de
vie des demandes, la consommation et la restitution par commande (idempotence,
pénurie partielle, annulation avant consommation) et l'isolation multi-tenant.

> ⚠️ **Aucune CI n'est configurée sur ce projet** — les tests doivent être lancés
> manuellement.

---

## Notes

- Le mapping DTO est fait à la main (extensions `ToDto()`) plutôt qu'avec
  AutoMapper/Mapster : moins de réflexion, plus lisible pour ce POC.
- L'image Docker pèse ~182 Mo (base `aspnet:8.0-alpine`), au-dessus de la cible
  de 150 Mo — c'est un plancher difficilement réductible en .NET.
