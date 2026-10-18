# FR-09 configuration checks

Requires .NET 10 and `TEST_CONNECTION_STRING` pointing to a PostgreSQL test
database with the repository migrations already applied. Run:

```sh
dotnet run --project tests/CivicConnect.ConfigurationChecks
```

The checks insert test records inside a transaction and roll back. They verify
async category/location deactivation, sync key-only asset deactivation, persisted
details and foreign keys, historical navigation loading, active selection queries,
and reactivation. Database identity sequences may advance.

FR-09 currently covers Category, Location, and Asset, which already have
IsActive columns. Remove/RemoveRange followed by either SaveChanges path becomes
deactivation. Existing unfiltered DbSets support historical and management reads;
ActiveCategories, ActiveLocations, and ActiveAssets are for new-selection queries.
No global query filters hide historical records. ExecuteDelete and raw SQL bypass
SaveChanges and must not be used to delete these configuration records.

This branch has no configuration delete endpoint, no editable database-backed
configuration UI, and no persisted ticket entity. Its requester category options
and tickets are demo data. Connecting those pages to persisted configuration is
outside this change; the checks use the existing asset/location relationship to
verify retained references. Database-backed ticket history needs verification
when that integration exists.
