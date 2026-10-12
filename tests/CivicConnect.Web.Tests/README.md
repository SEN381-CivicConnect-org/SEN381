# FR-06 priority override tests (issue #110)

Run with the repository's .NET 10 SDK requirement:

```sh
dotnet build
dotnet test tests/CivicConnect.Web.Tests/CivicConnect.Web.Tests.csproj
```

These xUnit tests use a unique EF Core in-memory database per case. They do not
read a connection string, migrate PostgreSQL, or start the application's demo
services. Controller tests verify stored data and check every mapped incident
scalar for mutation on rejection, including incidents with a prior override.
HTTP tests host the real controller with MVC and authorization middleware in
TestServer, using a test-only authentication handler.

Coverage includes priorities 1–4, preserved computed priority, trimmed reason,
UUID identity (`NameIdentifier` and `sub`), UTC override timestamp, reason and
priority validation, missing incidents, stale and optional versions, missing
priority targets, authorized roles, anonymous requests and forbidden roles.

The in-memory provider does not exercise PostgreSQL triggers, foreign keys or
the relational reload path. Version-conflict tests verify the controller's
explicit request-version check.

Running `dotnet test` for the whole solution also runs the existing identity
tests, whose fixture requires `CONNECTION_STRING` pointing to an isolated
PostgreSQL database and applies migrations. Use a disposable test database.
