using NpgsqlTypes;

namespace CivicConnect.Data.Entities;

/// The kinds of change an audit_log entry can record.
public enum AuditAction
{
    [PgName("CREATE")]
    Create,

    [PgName("UPDATE")]
    Update,

    [PgName("DEACTIVATE")]
    Deactivate,

    [PgName("OVERRIDE")]
    Override,
}
