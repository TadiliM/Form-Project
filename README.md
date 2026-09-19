# Form-Project

Plateforme de création de formulaires personnalisés avec gestion d'abonnements Premium.

## Fonctionnalités

- Création de formulaires avec champs personnalisés (texte, choix multiple, numérique)
- Authentification sécurisée (JWT + hash BCrypt)
- Système d'abonnement Premium via Stripe (paiement récurrent)
- Dashboard de visualisation des réponses aux formulaires

## Stack technique

- **Backend** : ASP.NET Core (.NET 10), Entity Framework Core
- **Base de données** : PostgreSQL 16
- **Authentification** : JWT (JSON Web Token)
- **Paiement** : Stripe (Checkout Sessions + Webhooks)
- **Conteneurisation** : Docker / Docker Compose

## Architecture

- `Models/` — entités métier (User, Form, Field avec héritage TPH, FormResponse, Answer, Subscription)
- `Data/` — configuration EF Core (AppDbContext, relations, migrations)
- `Services/` — logique métier (AuthService, PaymentService, FormService)
- `Controllers/` — endpoints API REST

## Démarrage local

\`\`\`bash
docker compose up -d --build
dotnet ef database update
\`\`\`

L'API est accessible sur `http://localhost:5050`.

## Variables d'environnement requises

Voir `appsettings.Development.json` (non versionné) pour :
- Chaîne de connexion PostgreSQL
- Clé secrète JWT
- Clés API Stripe (Secret, Publishable, Webhook Secret)