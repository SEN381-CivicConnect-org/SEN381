using System.Net;
using System.Text.Json;
using Xunit;

namespace CivicConnect.Tests.BlackBox;

// Test basis: FR-12 / source #33 / test issue #116. All verdicts come from HTTP.
public sealed class OverdueVisibilityTests : IClassFixture<OverdueHttpFixture>
{
    private readonly OverdueHttpFixture fixture;
    public OverdueVisibilityTests(OverdueHttpFixture fixture) => this.fixture = fixture;

    [Fact(DisplayName = "FR12-BB-01 — Past due date (Equivalence Partitioning)")]
    public async Task PastDueDate()
    {
        var due = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var id = await fixture.SeedAsync(due);
        AssertOverdue(await ReadAsync(), id, due);
    }

    [Fact(DisplayName = "FR12-BB-02 — Future due date (Equivalence Partitioning)")]
    public async Task FutureDueDate()
    {
        var due = new DateTimeOffset(2099, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(DateTimeOffset.UtcNow < due, "Fixed future-date fixture must be renewed after 2099.");
        var id = await fixture.SeedAsync(due);
        var controlDue = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var control = await fixture.SeedAsync(controlDue);
        var rows = await ReadAsync();
        AssertOverdue(rows, control, controlDue);
        AssertAbsent(rows, id);
    }

    [Fact(DisplayName = "FR12-BB-03 — No due date (Equivalence Partitioning)",
        Skip = "Current dev requires incident.due_at and derives it on insert. No public endpoint can create a no-due-date incident; this visibility precondition is unreachable.")]
    public void NoDueDate() { }

    [Fact(DisplayName = "FR12-BB-04 — Just before boundary (Boundary Value Analysis)")]
    public async Task JustBeforeBoundary()
    {
        // Warm the endpoint before measuring; setup occurs before the evaluation window.
        await ReadAsync();
        var id = await fixture.SeedAsync(DateTimeOffset.UtcNow.AddDays(-1));
        var anchor = MicrosecondNow();
        var due = anchor.AddSeconds(-1);
        await fixture.SetDueAsync(id, due);
        var rows = await ReadAsync();
        AssertWithinWindow(anchor);
        var row = AssertOverdue(rows, id, due);
        Assert.InRange(row.GetProperty("overrunMinutes").GetDouble(), 1d / 60, 2d / 60);
    }

    [Fact(DisplayName = "FR12-BB-05 — Just after boundary (Boundary Value Analysis)")]
    public async Task JustAfterBoundary()
    {
        await ReadAsync();
        var id = await fixture.SeedAsync(DateTimeOffset.UtcNow.AddDays(-1));
        var controlDue = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var control = await fixture.SeedAsync(controlDue);
        var anchor = MicrosecondNow();
        await fixture.SetDueAsync(id, anchor.AddSeconds(1));
        var rows = await ReadAsync();
        AssertWithinWindow(anchor);
        AssertOverdue(rows, control, controlDue);
        AssertAbsent(rows, id);
    }

    [Theory(DisplayName = "FR12-BB-06 — Finished incidents excluded (Equivalence Partitioning)")]
    [InlineData("RESOLVED")]
    [InlineData("CLOSED")]
    public async Task FinishedIncidents(string status)
    {
        var due = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var active = await fixture.SeedAsync(due);
        var finished = await fixture.SeedAsync(due, status);
        var rows = await ReadAsync();
        AssertOverdue(rows, active, due); // Positive control prevents an empty-list false pass.
        AssertAbsent(rows, finished);
    }

    [Fact(DisplayName = "FR12-BB-07 — Reporter cannot view management overdue list")]
    public async Task ReporterDenied()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/incidents/overdue");
        request.Headers.Add("X-FR12-Role", "Reporter");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<JsonElement[]> ReadAsync()
    {
        using var response = await fixture.Client.GetAsync("/api/incidents/overdue");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private static JsonElement AssertOverdue(JsonElement[] rows, long id, DateTimeOffset due)
    {
        var row = Assert.Single(rows, row => row.GetProperty("id").GetInt64() == id);
        Assert.True(row.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(due, row.GetProperty("dueAt").GetDateTimeOffset());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("referenceNumber").GetString()));
        Assert.True(row.GetProperty("overrunMinutes").GetDouble() > 0);
        return row;
    }

    private static void AssertAbsent(JsonElement[] rows, long id) =>
        Assert.DoesNotContain(rows, row => row.GetProperty("id").GetInt64() == id);

    private static DateTimeOffset MicrosecondNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private static void AssertWithinWindow(DateTimeOffset anchor) =>
        Assert.True(DateTimeOffset.UtcNow >= anchor && DateTimeOffset.UtcNow < anchor.AddSeconds(1),
            "Boundary precondition lost: setup + HTTP exceeded one second or the clock moved backwards. This is an infrastructure failure, not a visibility verdict.");
}
