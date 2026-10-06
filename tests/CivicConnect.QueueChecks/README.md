Run the FR-15 in-memory service checks with the .NET 8 SDK:

```sh
dotnet run --project tests/CivicConnect.QueueChecks/CivicConnect.QueueChecks.csproj
```

The executable exits with an error if an assertion fails. It covers every searchable field, category/status filters, both priority and date sort directions, combined queries, empty controls, no matches, active-only scope, newly created timestamps, and rendering the queue component with its labelled controls and active requests.

These checks do not verify database-backed queries. Ticket/incident persistence is not implemented yet.
