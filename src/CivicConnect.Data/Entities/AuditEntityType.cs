using NpgsqlTypes;

namespace CivicConnect.Data.Entities;

/// The kinds of entity an audit_log entry can describe.
public enum AuditEntityType
{
    [PgName("USER")]
    User,

    [PgName("ROLE_GRANT")]
    RoleGrant,

    [PgName("CATEGORY")]
    Category,

    [PgName("LOCATION")]
    Location,

    [PgName("ASSET")]
    Asset,

    [PgName("INCIDENT")]
    Incident,
}
