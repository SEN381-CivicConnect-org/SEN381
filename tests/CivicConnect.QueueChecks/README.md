Run with the .NET 10 SDK:

```sh
dotnet run --project tests/CivicConnect.QueueChecks/CivicConnect.QueueChecks.csproj
```

These checks compile the actual Staff Queue, shared TicketTable, model, service,
and filter helper using linked source files. They run without database startup
or the application's authentication dependencies and require no test packages.

Coverage includes trimmed and case-insensitive keyword search, category/status
filters, combined criteria, both priority/date directions, relative dates,
Created fallback, unknown dates, restricted input collections, the Staff role
attribute, rendered controls, active-ticket rows, and the empty state.
