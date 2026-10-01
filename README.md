# Form-Project

A form builder with custom fields and a paid Pro subscription. Users create forms,
share them through a public link, collect responses, and upgrade from the Free plan
through Stripe Checkout.

## Features

- Email/password accounts secured with JWT and BCrypt password hashing
- Form builder with three field types: text, single choice and number
- Public form pages addressed by slug — respondents do not need an account
- Response dashboard for form owners
- Freemium model: the Free plan is limited to 3 forms, the Pro plan removes the limit
- Stripe Checkout for subscriptions and signed webhooks for their lifecycle
  (`checkout.session.completed`, `customer.subscription.deleted`, `invoice.paid`)


## Tech stack

- **Backend:** ASP.NET Core (.NET 10), Entity Framework Core
- **Database:** PostgreSQL 16
- **Authentication:** JWT (7-day tokens), BCrypt password hashing
- **Payments:** Stripe (Checkout Sessions + webhooks)
- **Containers:** Docker / Docker Compose
- **Tests:** xUnit + Testcontainers (real PostgreSQL)

## Architecture

```
backend/
├── backend/                  # API project
│   ├── Models/               # Entities: User, Form, Field (TPH: Text/Choice/Number),
│   │                         #   FormResponse, Answer, Subscription
│   ├── Data/                 # AppDbContext: relations, cascades, migrations
│   ├── Services/             # Business logic: AuthService, UserService,
│   │                         #   FormService, SubscriptionsService (+ interfaces)
│   ├── Controllers/          # REST endpoints
│   └── Migrations/           # EF Core migrations
└── backend.Tests/            # Integration tests for the services
```

Controllers only translate HTTP to service calls: business rules (validation,
ownership checks, plan limits, slug generation) live in the services and models.


## API overview

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| POST | `/api/auth/register` | — | Creates an account and returns a JWT |
| POST | `/api/auth/login` | — | Returns a JWT and the profile |
| GET | `/api/forms` | JWT | Lists the caller's forms |
| POST | `/api/forms` | JWT | Creates a form (Free plan: max 3) |
| GET | `/api/forms/{id}` | JWT | Form detail with its fields |
| PUT | `/api/forms/{id}` | JWT | Updates title and fields |
| DELETE | `/api/forms/{id}` | JWT | Deletes the form and its responses |
| GET | `/api/forms/{id}/responses` | JWT | Responses with field labels |
| GET | `/api/forms/public/{slug}` | — | Public form definition |
| POST | `/api/forms/public/{slug}/responses` | — | Submits a response |
| GET | `/api/users/me` | JWT | Current profile (up-to-date plan) |
| PUT | `/api/users/me` | JWT | Updates the display name |
| POST | `/api/subscriptions/checkout` | JWT | Creates a Stripe Checkout session |
| POST | `/api/subscriptions/cancel` | JWT | Cancels the active subscription |
| POST | `/api/subscriptions/webhook` | Stripe signature | Receives Stripe events |

Errors are returned as `{ "message": "..." }`: `404` not found, `400` invalid
operation, `401` bad credentials, `409` email already in use.

## Getting started

### Prerequisites

- .NET SDK 10
- Docker (Docker Desktop) — for PostgreSQL and for the test suite
- The EF Core CLI if you need to apply migrations: `dotnet tool install --global dotnet-ef`

### Configuration

1. Create a `.env` file at the repository root with the database credentials used
   by Docker Compose:

   ```
   POSTGRES_DB=formproject
   POSTGRES_USER=formproject
   POSTGRES_PASSWORD=change-me
   ```

2. Create `backend/backend/appsettings.Development.json` (not versioned) with the
   connection string, the JWT settings and the Stripe keys:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5433;Database=formproject;Username=formproject;Password=change-me"
     },
     "Jwt": {
       "Key": "a-long-random-secret-at-least-32-characters",
       "Issuer": "form-project",
       "Audience": "form-project-clients"
     },
     "Stripe": {
       "SecretKey": "sk_test_...",
       "PublishableKey": "pk_test_...",
       "PriceId": "price_...",
       "WebhookSecret": "whsec_..."
     }
   }
   ```

### Run the stack

```bash
docker compose up -d --build
\`\`\`
dotnet ef database update --project backend/backend
```

The API is then available at `http://localhost:5050` and PostgreSQL at
`localhost:5433`. To run the API without Docker (database still in Docker), use the
`http` launch profile, which listens on `http://localhost:5095`:

```bash
dotnet run --project backend/backend
```

### Run the tests

```bash
dotnet test backend/backend.Tests/backend.Tests.csproj
```

The suite uses Testcontainers: it starts a throwaway `postgres:16` container, gives
every test its own database, and covers the services (64 tests). Docker must be
running.

## Plans and limits

| | Free | Pro |
| --- | --- | --- |
| Forms | 3 | Unlimited |
| Price | — | Recurring monthly via Stripe |

The plan is read from the database on every call, so a subscription change takes
effect immediately — the `planType` claim inside a JWT is only a snapshot from
login time.

## Documentation

- `frontendTasks.md` — what remains to build the frontend (design, API integration, deployment)
- `MVP.md` — MVP scope and success criteria
- `TestsBackend.md` — how the test suite works and what it covers
- `NextStep.md`, `CorrectionPaiement.md`, `Afaire.md` — working notes