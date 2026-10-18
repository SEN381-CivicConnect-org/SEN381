namespace CivicConnect.Data.Entities;

/// A technician's resolution of an incident; resolving again supersedes rather than overwrites.
public sealed class Resolution
{
    public long Id { get; set; }

    public long IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    public short ResolutionCodeId { get; set; }
    public ResolutionCode ResolutionCode { get; set; } = null!;

    public string Summary { get; set; } = null!;

    /// Required when the resolution code implies replacement.
    public int? AssetId { get; set; }
    public Asset? Asset { get; set; }

    /// Set by the database from the resolution code's ImpliesReplacement at insert time.
    public bool NeedsReplacement { get; set; }

    public Guid ResolvedBy { get; set; }
    public AppUser ResolvedByUser { get; set; } = null!;

    public DateTimeOffset ResolvedAt { get; set; }

    /// Null while this is the incident's active resolution.
    public DateTimeOffset? SupersededAt { get; set; }
}
