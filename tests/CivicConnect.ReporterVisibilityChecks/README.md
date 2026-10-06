Run the FR-11 demo-service checks with the .NET 8 SDK:

```sh
dotnet run --project tests/CivicConnect.ReporterVisibilityChecks/CivicConnect.ReporterVisibilityChecks.csproj
```

This executable exits with an error if any assertion fails. It checks IT-VIS-01 OwnHistoryOnly, IT-VIS-02 ReadYourWritesAfterSubmit, ordering, current status, reporter detail ownership, authenticated identity requirements, and the 20-request history limit.

These checks exercise the in-memory service. Database-backed verification depends on the documented ticket/incident persistence, `v_ticket_status`, and `ticket_reporter_submitted_idx`, which are not yet implemented.
