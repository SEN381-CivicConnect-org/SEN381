using NpgsqlTypes;

namespace CivicConnect.Data.Entities;

/// The kinds of incident events a subscriber can be notified about.
public enum NotificationKind
{
    [PgName("STATUS_CHANGE")]
    StatusChange,

    [PgName("ASSIGNMENT")]
    Assignment,

    [PgName("RESOLUTION")]
    Resolution,
}
