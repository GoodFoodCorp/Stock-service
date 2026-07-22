# Stock Service

Microservice .NET 8 de gestion des stocks et du réapprovisionnement pour les
franchisés Good Food 3.0. **Clean Architecture + CQRS (MediatR)**.

## Architecture (Clean Architecture en couches)

```
StockService.slnx
src/
├── Core/
│   ├── Stock.Domain/        # Entités (StockItem, StockMovement, ReplenishmentRequest),
│   │                        # règles métier, erreurs typées, interfaces repositories
│   │                        # → ZÉRO dépendance externe (pas d'EF Core ici)
│   └── Stock.Application/    # CQRS : un Command/Query + Handler + Validator par cas
│                            # d'usage (FluentValidation via pipeline MediatR)
├── Infrastructure/
│   └── Stock.Infrastructure/ # DbContext EF Core (PostgreSQL), migrations, repositories
└── Presentation/
    └── Stock.Api/           # Contrôleurs fins (une ligne : mediator.Send), JWT,
                             # middleware d'erreurs, Serilog JSON, Swagger
tests/
├── Stock.Domain.Tests/      # xUnit + FluentAssertions (23 tests)
└── Stock.Application.Tests/ # + Moq sur les handlers (9 tests)
```

## Règles métier clés

- Tout changement de quantité passe par `StockItem.ApplyMovement` (IN / OUT /
  ADJUSTMENT) → audit trail complet dans `stock_movements`.
- **Réappro automatique** : si un mouvement OUT fait passer `QuantityOnHand`
  sous `ThresholdMin`, une `ReplenishmentRequest` PENDING est générée
  (quantité = `ThresholdMax − QuantityOnHand`), sans doublon si une demande
  est déjà en attente.
- Cycle de vie d'une demande : `PENDING → APPROVED → ORDERED → RECEIVED`
  (`CANCELLED` possible avant réception). **RECEIVED ré-injecte la quantité
  en stock** (mouvement IN automatique).
- Multi-tenant : un manager ne voit/modifie que le stock de son restaurant
  (`tenant_id` du JWT) ; le siège (`admin`) peut tout voir et cibler un tenant
  via `?tenantId=`.

## Prérequis & lancement

```bash
docker network create microservices-net   # une fois (partagé entre services)
cp .env.example .env                      # POSTGRES_PASSWORD + JWT_SECRET
                                          # ⚠️ JWT_SECRET identique au auth-service
docker compose up -d --build
```

Migrations EF Core appliquées automatiquement au démarrage.

## Variables d'environnement

| Variable | Requis | Description |
|---|---|---|
| `POSTGRES_USER/PASSWORD/DB` | oui | Credentials de la base dédiée `stock-db` (port hôte 5434) |
| `JWT_SECRET` | oui | Secret HS256 **identique au auth-service** |
| `ConnectionStrings__StockDb` | hors docker | Chaîne Npgsql (le compose la construit) |

## Endpoints (port 8083)

Swagger UI : `GET /docs`

| Méthode | Route | Rôles |
|---|---|---|
| GET | `/api/stocks` | manager, admin |
| GET | `/api/stocks/{id}` (détail + mouvements) | manager, admin |
| POST | `/api/stocks` | manager, admin |
| POST | `/api/stocks/{id}/movements` | manager, admin |
| GET | `/api/replenishment-requests` | manager, admin |
| POST | `/api/replenishment-requests` | manager, admin |
| PATCH | `/api/replenishment-requests/{id}/status` | admin (décision siège) |
| GET | `/healthz`, `/readyz` | public (probes K8s) |

## Tests

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test   # 32 tests (SDK ≥ 8)
```

## Notes

- Mapping DTO manuel (extensions `ToDto()`) plutôt que Mapster/AutoMapper :
  moins de réflexion, plus lisible pour un POC — à réévaluer si les modèles grossissent.
- `POST /api/stocks` est un ajout au contrat initial (nécessaire pour créer le
  catalogue d'articles depuis le back-office).
