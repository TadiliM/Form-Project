# Frontend Tasks — Form-Project

The backend API is in place; no frontend exists yet. This document is the complete
checklist to build it: design, stack choices, API integration, testing and
deployment. It is written to be followed top to bottom.

---

## 1. Product recap

Form-Project is a form builder with a freemium model.

Two kinds of users:

- **Creator** — creates an account, builds forms, shares a public link, reads the
  responses, and can upgrade to Pro through Stripe.
- **Respondent** — opens the public link and answers without an account.

Main flows to support:

1. Register / log in → dashboard.
2. Create a form (title + fields) → get a shareable public URL.
3. Open the public URL → fill and submit a response.
4. See the responses in the dashboard (newest first, with field labels).
5. Upgrade to Pro via Stripe Checkout / cancel the subscription.
6. Edit name and see the current plan in the profile page.

Freemium rule enforced by the API: the Free plan is limited to **3 forms**; Pro is
unlimited.

---

## 2. Design phase

### 2.1 Screens to design

| # | Screen | Route (suggested) | Auth | Purpose |
| --- | --- | --- | --- | --- |
| 1 | Landing page | `/` | — | Pitch, features, Free/Pro pricing, sign-up CTA |
| 2 | Register | `/register` | — | Email, name, password form |
| 3 | Login | `/login` | — | Email, password form |
| 4 | Dashboard | `/dashboard` | JWT | List of forms with counters and plan badge |
| 5 | Form builder | `/forms/new` | JWT | Title + dynamic field editor |
| 6 | Form detail | `/forms/[id]` | JWT | Edit title/fields, share link, responses tab |
| 7 | Responses | `/forms/[id]/responses` | JWT | Table of responses with labels and dates |
| 8 | Public form | `/f/[slug]` | — | Render fields from the public API, submit |
| 9 | Submission success | `/f/[slug]/thanks` | — | Confirmation after a response is stored |
| 10 | Profile / plan | `/account` | JWT | Name, email, plan, upgrade/cancel buttons |
| 11 | Checkout success | `/success` | JWT | Stripe return page; refetch the profile |
| 12 | Checkout cancel | `/cancel` | JWT | Stripe return page when the payment is abandoned |

Routes `/success` and `/cancel` are imposed by the backend: the Stripe Checkout
session redirects to `http://localhost:3000/success` and
`http://localhost:3000/cancel`. The frontend must run on port **3000** in
development, or these URLs must become configurable on the backend (see §6).

### 2.2 States every screen must handle

- **Loading** — skeletons for lists, spinner for buttons.
- **Empty** — first-run dashboard (“no forms yet”) and no-responses state.
- **Error** — display the `message` returned by the API; keep the user’s input.
- **Success** — snackbar/confirmation, then redirect (deletion, save, submission).
- **Not found** — unknown form id (dashboard) or unknown slug (public page).
- **Plan limit** — the API refuses the 4th Free form with
  `Free plan limit reached (3 forms). Upgrade to the Pro plan to create more.`
  Show it next to an “Upgrade to Pro” CTA, not as a generic error.

### 2.3 Form builder rules (must match the API)

- Title: required, trimmed; a form with no field is rejected.
- Field types: `text`, `choice`, `number`.
- Shared per field: `label` (required), `isRequired`, `order`.
- `text`: optional `maxLength`, default 500, must be > 0.
- `choice`: `options`, at least one non-empty value; blanks are dropped.
- `number`: optional `min` / `max`, defaults `0` / `decimal.MaxValue`;
  `min` must not exceed `max`.
- Once a form has received a response, the API refuses any field change:
  freeze the builder and show an explanation instead.
- Field reordering is client-side: write the final order into each field’s `order`.

### 2.4 Respondent page rules

- Fetch the form by slug, render fields in `order`, honour `isRequired`.
- Validate on the client first, but treat the API as the source of truth:
  required fields, choice values inside `options`, number inside `[min, max]`,
  text length ≤ `maxLength`.
- Send numbers as plain digit strings (e.g. `"12.5"`); the API parses them as
  decimals.
- Optional fields left blank can be omitted from the payload.
- Answers must reference field ids returned by `GET /api/forms/public/{slug}`.

### 2.5 Design system and UX decisions to make

- Component library: Tailwind CSS + shadcn/ui, MUI or Chakra — pick one and keep
  it consistent; no bespoke CSS framework on top.
- Responsive breakpoints: mobile-first; the builder is the densest screen.
- Accessibility: every input has a real `<label>`; required state is text, not
  colour only; errors use `aria-describedby` and `role="alert"`.
- Language: the product and API messages are in English; decide whether to add
  i18n from the start (e.g. `next-intl`) or ship English only.
- Branding: logo, colour palette, typography, dark mode (optional).

Deliverables for this phase: a page inventory (above), low-fidelity wireframes for
the six main screens, and the component list (button, input, select, textarea,
field-row, empty state, plan badge, dialog).

---

## 3. Stack decisions

Recommended defaults (adjust if there is a reason):

| Topic | Choice | Why |
| --- | --- | --- |
| Framework | **Next.js (App Router) + TypeScript** | Port 3000 by default (Stripe redirects), file-based routes for `/f/[slug]` and `/success`, easy deployment |
| Server state | TanStack Query (or SWR) | Caching, refetch-on-focus, polling after Stripe |
| Forms | react-hook-form + zod | Dynamic field arrays, validation shared between screens |
| Styling | Tailwind CSS + shadcn/ui | Fast to build, accessible primitives |
| Client | Typed `fetch` wrapper | The API has no live OpenAPI document yet (§6) |
| Tests | Vitest + Testing Library, Playwright for E2E | Unit + full user journey |

State to keep client-side: the JWT and the current user profile. Everything else
(forms, responses) is server state and should be refetched after mutations.

Auth storage: the API returns the token in the response body and has no refresh
endpoint (7-day expiry). Simplest robust option for an SPA: keep the token in
memory + `localStorage` with a global `401` handler that logs the user out and
redirects to `/login`. `httpOnly` cookies would require a backend change.

Project layout to plan:

```
frontend/
├── app/                  # routes (landing, dashboard, forms, f/[slug], account)
├── components/           # UI primitives + feature components
├── lib/api.ts            # fetch wrapper, token handling, error mapping
├── lib/types.ts          # DTO types mirroring the backend
└── lib/validation.ts     # field validation shared by builder and respondent page
```

Configuration: `NEXT_PUBLIC_API_BASE_URL` (default `http://localhost:5050`).

---

## 4. API integration

### 4.1 Contracts the frontend depends on

**Auth**

| Call | Body | Response |
| --- | --- | --- |
| `POST /api/auth/register` | `{ email, name, password }` | `200 { token, email, name, planType }` — `409 { message }` if the email is taken |
| `POST /api/auth/login` | `{ email, password }` | `200 { token, email, name, planType }` — `401 { message }` on bad credentials |

All JWT-protected calls send `Authorization: Bearer <token>`.

**Forms (creator)**

| Call | Body | Response |
| --- | --- | --- |
| `GET /api/forms` | — | `FormSummaryDto[]`: `{ id, title, publicUrlSlug, createdAt, fieldCount, responseCount }` |
| `POST /api/forms` | `{ title, fields: FieldRequest[] }` | `201 FormSummaryDto` + `Location` header |
| `GET /api/forms/{id}` | — | `FormDetailDto`: `{ id, title, publicUrlSlug, createdAt, responseCount, fields: FieldResponse[] }` |
| `PUT /api/forms/{id}` | `{ title, fields: FieldRequest[] }` | `200 FormDetailDto`; `400` if responses already exist |
| `DELETE /api/forms/{id}` | — | `204` |
| `GET /api/forms/{id}/responses` | — | `[{ id, submittedAt, answers: [{ fieldId, label, value }] }]`, newest first |

`FieldRequest`: `{ type, label, isRequired, order, maxLength?, options?, min?, max? }`.
`FieldResponse`: `{ id, type, label, isRequired, order, maxLength?, options?, min?, max? }`.
Only the properties relevant to `type` are set; the others are `null`.

**Public (no token)**

| Call | Body | Response |
| --- | --- | --- |
| `GET /api/forms/public/{slug}` | — | `FormDetailDto`; `404` on an unknown slug |
| `POST /api/forms/public/{slug}/responses` | `{ answers: [{ fieldId, value }] }` | `201 { id }` |

**User and subscription**

| Call | Body | Response |
| --- | --- | --- |
| `GET /api/users/me` | — | `{ id, email, name, planType, createdAt }` |
| `PUT /api/users/me` | `{ name }` | `200 UserProfileDto` |
| `POST /api/subscriptions/checkout` | — | `200 { checkoutUrl }` → redirect the browser to it |
| `POST /api/subscriptions/cancel` | — | `202` empty; `400 { message }`; `500` on a Stripe failure |

`planType` is `"Free"` or `"Pro"`.

### 4.2 Error handling

- Every error body is `{ "message": "..." }` (or empty for `500`).
- Status mapping: `400` invalid input, `401` not authenticated (redirect to
  login), `404` unknown resource, `409` email already used, `500` server/Stripe
  error (generic message + retry).
- Show API messages as-is: they are user-facing and already in English.
- Never call `POST /api/subscriptions/webhook` from the browser — it is
  server-to-server and requires a Stripe signature.

### 4.3 Behaviour details that influence the UI

- **Stale plan in the JWT**: the token’s `planType` is a snapshot from login.
  Always read the plan from `GET /api/users/me`, never from the token.
- **Webhook latency**: after Checkout, the plan changes when Stripe delivers the
  webhook. On `/success`, refetch `GET /api/users/me` every few seconds for a
  short window before showing the confirmation.
- **Share link**: build it as `<frontend>/f/<publicUrlSlug>` and offer a copy
  button. The slug is stable; two forms may share a title but never a slug.
- **Responses**: only the creator can read them; the table columns are the form
  fields. Use the `label` returned with each answer (it falls back to
  `(deleted field)` if a field disappeared).
- **Cancellation is asynchronous**: `202` means Stripe accepted the request; the
  webhook updates the local state afterwards. Refetch the profile after a short
  delay.
- **Form id vs slug**: the dashboard uses numeric ids (`/api/forms/{id}`); the
  public page only knows the slug.

### 4.4 Backend gaps to close before/while building the frontend

These are backend changes the frontend needs; none of them are done yet.

1. **CORS**: `Program.cs` registers no CORS policy. Add an `AddCors` policy that
   allows the frontend origin (`http://localhost:3000` in development, the
   deployed domain in production) with the `Authorization` header, then
   `app.UseCors(...)`. Otherwise every browser call fails, even though the same
   call works from curl. Alternatively, proxy `/api` through Next.js rewrites.
2. **OpenAPI**: the OpenApi package is referenced but `AddOpenApi`/`MapOpenApi`
   are never called, so there is no schema to generate a client from. Either map
   it and generate a typed client (`openapi-typescript`), or keep the hand-written
   types in `lib/types.ts` in sync with this document.
3. **Stripe redirect URLs**: `SuccessUrl`/`CancelUrl` are hardcoded to
   `http://localhost:3000`. Move them to configuration
   (`Stripe:SuccessUrl`, `Stripe:CancelUrl`) before any non-local deployment.
4. **Missing endpoints to consider later**: password reset, email verification,
   pagination for forms/responses, response export (CSV), Stripe Billing Portal
   link for self-service plan management. None are required for the MVP.

---

## 5. Milestones

Build the frontend in this order; each milestone is demoable on its own.

1. **M0 — Skeleton**: Next.js project, Tailwind, API client, env config, layout
   and navigation, 404 page. Done when a request to `GET /api/forms/public/xxx`
   shows a styled 404 state.
2. **M1 — Authentication**: register and login screens, token storage, protected
   routes, logout, `401` handling. Done when a registered user reaches the empty
   dashboard and survives a page reload.
3. **M2 — Dashboard**: form list with counters, empty state, create button,
   delete with confirmation, plan badge and limit message. Done when the Free
   limit is shown properly on the 4th creation attempt.
4. **M3 — Builder**: create/edit screens with dynamic fields, type-specific
   editors, validation, save, reorder. Done when a form with one field of each
   type round-trips through the API.
5. **M4 — Public page**: render by slug, validation, submit, thanks page, not
   found state. Done when an anonymous visitor can respond end to end.
6. **M5 — Responses**: responses tab/table with labels, newest first, empty
   state, refresh. Done when a new response appears without reloading the app.
7. **M6 — Account and subscription**: profile page, name update, upgrade button
   (Checkout redirect), `/success` polling, cancel with confirmation. Done when
   the plan badge goes Free → Pro → Free using a Stripe test card.
8. **M7 — Quality**: loading/error states everywhere, accessibility pass,
   Playwright journey, responsive check.
9. **M8 — Deployment**: production build, environment variables, CORS,
   redirect URLs, hosted frontend.

Acceptance test for the whole MVP (already stated in `MVP.md`): one user can
register, create a form, share it, receive a response and read it — without an
error — and a full Stripe test payment flips the account to Pro.

---

## 6. Testing the frontend

- **Unit**: validation helpers (the rules of §2.3), API error mapping, dynamic
  field rendering. Vitest + Testing Library.
- **End-to-end**: Playwright against the real stack
  (`docker compose up -d --build`, API on `:5050`, frontend on `:3000`):
  1. register → empty dashboard;
  2. create a form with three field types;
  3. copy the share link, open it in a clean context, submit a response;
  4. log back in, see the response with labels;
  5. start Checkout, pay with `4242 4242 4242 4242`, land on `/success`, see Pro.
- **Stripe locally**: run `stripe listen --forward-to localhost:5050/api/subscriptions/webhook`
  and put the printed `whsec_...` into `Stripe:WebhookSecret`. Without it, the
  plan never changes after Checkout.
- **Against the CI API**: the backend test suite needs Docker (Testcontainers);
  the frontend E2E job needs the compose stack plus a Stripe test key.

---

## 7. Deployment

### 7.1 What the backend already provides

- `backend/backend/Dockerfile` builds the API; `docker-compose.yml` runs
  `postgres` (5433:5432 on the host) and `backend` (5050:8080).
- Configuration comes from `appsettings.Development.json` (not versioned) and
  environment variables (`ConnectionStrings__DefaultConnection`, `Jwt__*`,
  `Stripe__*`).
- EF migrations live in `backend/backend/Migrations`.

### 7.2 Frontend deployment options

- **Vercel / Netlify** (simplest for Next.js): build with
  `NEXT_PUBLIC_API_BASE_URL=https://api.example.com`, no container needed.
- **Docker**: add a `frontend` service to `docker-compose.yml` (Next.js
  standalone output), build args for the public API URL. If both services are
  behind one domain, a reverse proxy (Nginx/Traefik) avoids CORS entirely.

Environment variables to define per environment: `NEXT_PUBLIC_API_BASE_URL`,
and the backend’s `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`,
`ConnectionStrings__DefaultConnection`, `Stripe__SecretKey`,
`Stripe__PublishableKey`, `Stripe__PriceId`, `Stripe__WebhookSecret`,
`Stripe__SuccessUrl`, `Stripe__CancelUrl` (after gap #3 in §4.4 is fixed).

### 7.3 Production checklist

- [ ] HTTPS on both frontend and API domains.
- [ ] CORS allowlist set to the frontend origin only.
- [ ] Stripe webhook endpoint registered on the public API URL
      (`https://api.example.com/api/subscriptions/webhook`) with the production
      signing secret.
- [ ] Stripe live keys and a real recurring Price.
- [ ] Database migrations applied at deploy time (`dotnet ef database update`).
- [ ] Secrets injected by the platform, never committed.
- [ ] Backups for PostgreSQL.
- [ ] CI: frontend lint + unit tests; backend build + `dotnet test` (Docker
      runner); deploy frontend and API only when green.
- [ ] Monitoring: API logs (Stripe webhook failures are logged), uptime check on
      `/api/forms/public/<slug>` of a smoke-test form.

---

## 8. Open questions to settle before starting

1. Next.js vs React + Vite — Next.js recommended because of port 3000 and the
   public routes.
2. Where to store the JWT (`localStorage` vs cookie) — the backend has no refresh
   or cookie endpoint today.
3. English-only UI, or i18n from day one?
4. Who owns the visual identity (logo, palette) — required before the design
   freeze.
5. Should the backend expose the Stripe Billing Portal for self-service
   cancellation instead of the custom cancel endpoint?