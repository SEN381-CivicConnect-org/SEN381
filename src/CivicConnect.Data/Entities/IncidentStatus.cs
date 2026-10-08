using NpgsqlTypes;

namespace CivicConnect.Data.Entities;

/// The incident lifecycle, from a new report to a closed, rejected or merged outcome.
public enum IncidentStatus
{
    [PgName("NEW")]
    New,

    [PgName("ASSIGNED")]
    Assigned,

    [PgName("IN_PROGRESS")]
    InProgress,

    [PgName("ON_HOLD")]
    OnHold,

    [PgName("RESOLVED")]
    Resolved,

    [PgName("CLOSED")]
    Closed,

    [PgName("REJECTED")]
    Rejected,

    [PgName("MERGED")]
    Merged,
}
