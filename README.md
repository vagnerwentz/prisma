# Prisma

**Personal finance built for how money actually moves in Brazil:** Pix, credit card statements with
real closing and due dates, installments that never lose a cent, and refunds that behave like refunds.

[![CI](https://github.com/vagnerwentz/prisma/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/vagnerwentz/prisma/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![React 19](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white)
![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)

The CI badge covers the whole pipeline: backend unit, property, integration and architecture tests,
frontend lint, unit tests and production build, and a backup round trip (dump, encrypt, restore,
compare). Deploys only happen when it is green.

## Why

Most finance apps treat a credit card purchase as money spent the day you swipe. In Brazil that is
rarely true: the purchase lands on a statement, the statement closes on a date the bank decides, and
the money leaves your account on the due date, sometimes split into installments. Prisma models that
reality, so the monthly numbers match your bank account instead of your receipts.

## What it does

- **Accounts:** checking, cash, investments and credit cards, with balances.
- **Credit card statements:** each purchase lands on the right statement from the card's closing and
  due days; statement dates can be adjusted when the bank moves them.
- **Aligned with the bank:** a purchase the bank posted to the next statement can be moved there
  (installment purchases move whole), and adjusting a statement's dates shows, before saving, which
  purchases would change statements. Both come with one-tap undo.
- **Wrong card? Change it:** a purchase logged on the wrong card moves to the other card's
  statements (installments move together), with the money leaving on the new due date.
- **Recurring entries:** a weekly or monthly Pix, or a charge a company makes on the card every month.
  Each occurrence is recorded only on its day; what is still to come shows up as a lighter projection in
  the months ahead and on the open statement, never in balances. A charge that would land on an
  already-paid statement waits for a decision instead of changing it.
- **Automatic debits:** utility bills the bank debits from a checking account. The debit lands on the
  next business day after the due date (weekends and national holidays). When the bill changes every
  month, the debit is recorded with an average amount, marked as an estimate everywhere it shows, and
  waits in a bell in the header until you confirm it or type the real amount.
- **Installments:** R$ 100.00 in 3x is 33.34 + 33.33 + 33.33, always adding up to the total.
- **Refunds** reduce spending in the month they land and are never counted as income.
- **Transfers** (including paying a card statement) are never income or expense, so nothing is
  counted twice.
- **Investments:** a portfolio of B3 stocks, REITs (FIIs) and ETFs picked from a catalog synced daily, and the
  payouts each one made (dividends, interest on equity, fund income), recorded as income in the account where the
  money landed. Company logos are downloaded,
  sanitized and served by the app itself.
- **Cash-flow dashboard:** what came in, what went out and what was invested each month, by
  settlement date; category breakdown, month-over-month explanation and upcoming commitments.
- **Categories:** a default set per user that evolves: new default categories reach existing users
  once, without undoing their renames or deletions.
- **Undo everywhere:** deleting is a soft delete, restorable from the toast.

## Engineering highlights

- **Money is an integer.** Amounts are `long` cents behind a `Money` value object; splitting into
  installments distributes the remainder and is verified by property-based tests over thousands of
  inputs.
- **Two dates per transaction.** `PurchaseDate` (when it happened) and `SettlementDate` (when the
  money leaves). Dates are `DateOnly` in `America/Sao_Paulo`, behind an `IClock`; an architecture test
  fails the build if any code reads the system clock directly.
- **Rich domain, thin handlers.** Business rules live in a domain project with no infrastructure
  dependencies (enforced by NetArchTest). API handlers load, delegate and save.
- **Isolation by default.** Every user row belongs to a user and a global query filter scopes every query;
  an architecture test forbids bypassing it, and another one rejects any table that has no owner unless it
  is on a short, explicit list of shared tables (login data and market data).
- **Concurrency on purpose.** Statements use PostgreSQL's `xmin` as a concurrency token, with race
  tests: paying a statement while a purchase moves into it returns 409 instead of a wrong total.
- **A background job that cannot duplicate.** Recurring entries are generated by an in-process
  `BackgroundService` that acts as each user (same query filters as a request). The entry and the
  series' progress are saved together, a unique index covers even deleted occurrences, and `xmin`
  guards the series: running twice, concurrently or after downtime catches up without duplicates.
- **A bank calendar with nothing to maintain.** Business days come from weekends, fixed national holidays
  and the ones computed from Easter (Carnival, Good Friday, Corpus Christi). Property-based tests check it
  across a century, and an architecture test limits which code may use it: only automatic debits and card
  due dates move to a business day; everything else keeps the date it happened.
- **Third-party data at arm's length.** The B3 asset list comes from an external provider behind an interface, synced
  once a day into a shared catalog: a list that suddenly shrinks is refused, assets that disappear are deactivated and
  never deleted, and tests replay recorded real responses instead of calling the provider. Company logos are SVGs from
  that provider, so they are sanitized (no scripts, external references or foreign elements) and served by the app
  under a locked-down content security policy.
- **Tests that prove themselves.** Rules about money and dates are tested before they are written,
  integration tests run against a real PostgreSQL (Testcontainers, never an in-memory provider), and
  new rules are checked by breaking them on purpose and watching a test fail.
- **Structured logs** with source-generated `LoggerMessage` events, one JSON line per event, and a
  test that refuses events carrying amounts, descriptions, names, emails or tokens.
- **Backups that have been restored, not just taken.** A daily job dumps the database as a read-only
  user, encrypts it with `age` before it leaves the container (the private key never touches the
  server) and stores it off the hosting provider, under a deletion lock. A truncated dump is never
  uploaded, a missed day sends an alert, and CI restores a backup on every push and compares it row by
  row. A restore drill has been run against production.

## How money moves

A card purchase is not money spent on the day you swipe. It lands on a statement, and the money leaves on
the statement's due date. That due date is the date the dashboard counts.

```mermaid
flowchart LR
    Buy["Purchase<br/><i>PurchaseDate</i>"] -->|"closing day decides"| Statement["Statement<br/>(open until it closes)"]
    Statement -->|"due date"| Cash["Money leaves the account<br/><i>SettlementDate</i>"]
    Cash --> Dashboard["Monthly cash flow<br/>(counted here)"]
    Pix["Pix, debit, cash"] -->|"same day"| Cash
```

An automatic debit repeats every month on its own. When the amount changes, the app records the average
and asks you to check it:

```mermaid
flowchart LR
    Due["Due date"] -->|"weekend or holiday?<br/>next business day"| Debit["Debit recorded<br/>with the average amount"]
    Debit --> Bell["Bell in the header<br/>≈ estimate to check"]
    Bell -->|"Confirm"| Exact["Month total is exact"]
    Bell -->|"Changed: type the real amount"| Exact
```

## Architecture

```mermaid
flowchart LR
    Web["React 19 + TypeScript<br/>TanStack Query"] -->|"/api (same origin, httpOnly cookie)"| Api
    subgraph Api["ASP.NET Core Minimal APIs"]
        Slices["Vertical slices<br/>one file per use case"]
    end
    Slices --> Domain["Prisma.Domain<br/>entities, value objects, rules<br/>(no dependencies)"]
    Slices --> Db[("PostgreSQL 18<br/>EF Core, snake_case")]
    Job["Background jobs<br/>recurring entries, automatic debits<br/>and the asset catalog"] --> Domain
    Job --> Db
    Job -->|"daily asset list and logos"| Brapi["brapi.dev<br/>(B3 market data)"]
```

- **`src/Prisma.Domain`:** entities, value objects and rules; no EF Core, ASP.NET Core or Npgsql.
- **`src/Prisma.Api`:** one file per use case (request, validator, handler and endpoint together),
  EF Core configuration, migrations, authentication.
- **`src/prisma-web`:** React app, served by the API in production (same origin, no CORS).
- **`tests/`:** domain, integration and architecture tests.

In production the API and the React build ship as one Docker image on Railway, with migrations applied
on startup and deploys gated on CI. A second service in the same project backs the database up every
night:

```mermaid
flowchart LR
    subgraph Railway["Railway project (private network)"]
        App["API + React<br/>one Docker image"] --> Pg[("PostgreSQL")]
        Cron["Backup service<br/>daily cron, 03:00 São Paulo"] -->|"pg_dump as a<br/>read-only user"| Pg
    end
    Cron -->|"encrypted with age<br/>(public key only)"| R2[("Cloudflare R2<br/>90-day retention<br/>30-day deletion lock")]
    Cron -->|"start, success, failure"| HC["Healthchecks.io<br/>alerts when a day is missed"]
    Key["Private key<br/>held only by the owner"] -.->|"restore"| R2
```

The backup lives outside Railway on purpose: losing the hosting account must not take the backups with it.

## Tech stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, FluentValidation |
| Auth | ASP.NET Core Identity, `httpOnly` secure cookie, rate limiting |
| Database | PostgreSQL 18 |
| Frontend | React 19, TypeScript, Vite, TanStack Query, React Hook Form + Zod, Tailwind CSS, shadcn/ui |
| API client | `openapi-typescript` + `openapi-fetch`, types generated from the OpenAPI document |
| Tests | xUnit, Shouldly, CsCheck, Testcontainers, NetArchTest, Vitest |
| Hosting | Railway (Docker), deploys gated on CI |
| Market data | brapi.dev (B3 asset list and logos), typed `HttpClient`, no SDK |
| Backups | `pg_dump` + `age` + `rclone` (Alpine image), Cloudflare R2, Healthchecks.io |

## Getting started

Requirements: .NET 10 SDK, Node.js, Docker.

```bash
dotnet tool restore
cp .env.example .env          # then set a database password
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=prisma;Username=prisma;Password=<password from .env>" \
  --project src/Prisma.Api

docker compose up -d           # PostgreSQL
dotnet ef database update -p src/Prisma.Api -s src/Prisma.Api
dotnet run --project src/Prisma.Api

cd src/prisma-web
npm install
npm run dev                    # http://localhost:5173, proxies /api to the API
```

Tests:

```bash
dotnet test                    # domain, integration (needs Docker) and architecture
cd src/prisma-web && npm test  # frontend

# backup round trip (needs Docker): the schema comes from the migrations
dotnet ef migrations script --idempotent -p src/Prisma.Api -s src/Prisma.Api -o /tmp/schema.sql
ops/backup/test/roundtrip.sh /tmp/schema.sql
```

## Project documents

The roadmap (`PLAN.md`), the phase specifications (`docs/`), the operations runbook
(`docs/operacao.md`: database users, backups, restore, logs) and the working agreement (`CLAUDE.md`)
are working documents written in Portuguese.

## License

Copyright © 2026 Vagner Wentz. All rights reserved.

This repository is public so the code can be read. No license is granted: it may not be copied,
modified, distributed or used, in whole or in part, without written permission.
