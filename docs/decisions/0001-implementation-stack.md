# 0001: Implementation stack and migration tool

**Status:** Accepted
**Date:** 2026-09-27

## Decision

CivicConnect is implemented in .NET 10 (current LTS). Schema migrations are managed with EF Core Migrations (`Microsoft.EntityFrameworkCore.Design`) against Npgsql (`Npgsql.EntityFrameworkCore.PostgreSQL`), driven by the `dotnet-ef` CLI as a pinned local tool (`.config/dotnet-tools.json`).

Table and column names use `snake_case`, matching the ERD, via the `EFCore.NamingConventions` package (`UseSnakeCaseNamingConvention()`). EF Core's internal migrations-history table is explicitly renamed to `__ef_migrations_history` to match. `EFCore.NamingConventions` does not rewrite that table's name automatically.

## Why

- The repository's `.gitignore` was already authored for a .NET/ASP.NET Core/Blazor + EF Core toolchain, so this formalises an already-implied choice rather than introducing a new one.
- EF Core Migrations satisfies every mechanical requirement in the migration-harness ticket: `dotnet ef database update` for up, `dotnet ef database update 0` for down, and migration state recorded in the target database (`__ef_migrations_history`), not the repo.
- `dotnet-ef` as a local tool (vs. a global install) means CI and every developer resolve the exact same tool version from `.config/dotnet-tools.json`, with no manual setup step beyond `dotnet tool restore`.

## Alternatives considered

- Plain versioned SQL files. Simplest and language-agnostic, but pushes all up/down script-writing and history tracking onto us by hand, all of which EF Core Migrations already gives for free once a .NET stack is picked.
- Node + Knex or node-pg-migrate. No other reason to bring a Node runtime into a project already committed to .NET via `.gitignore`.
- Java + Flyway or Liquibase. Same reasoning; no other part of the project points at a JVM stack.

## Consequences

- Every future migration is added with `dotnet ef migrations add <Name> --project src/CivicConnect.Data`, reviewed as a normal file in `src/CivicConnect.Data/Migrations/`, and is never hand-edited once merged (see [`0003-schema-conventions`](0003-schema-conventions.md)).
- Any future ASP.NET Core host project must configure its runtime `DbContext` through `CivicConnectDbContext.Configure(...)` so naming conventions and the history table name never drift between design-time (`dotnet ef`) and runtime.
