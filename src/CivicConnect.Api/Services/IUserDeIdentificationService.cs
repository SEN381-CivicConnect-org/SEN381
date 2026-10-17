namespace CivicConnect.Api.Services;

/// <summary>
/// Result DTO returned upon successful user de-identification (NFR-05, POPIA s14(1), IT-RET-02).
/// </summary>
public sealed record UserDeIdentificationResultDto
{
    /// <summary>
    /// Identifier of the de-identified user.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Anonymized tombstone email (e.g., <c>deidentified-{id}@redacted.local</c>).
    /// </summary>
    public string AnonymizedEmail { get; init; } = string.Empty;

    /// <summary>
    /// Generic replacement name (<c>De-identified User</c>).
    /// </summary>
    public string GenericFullName { get; init; } = string.Empty;

    /// <summary>
    /// Deactivation status (always <c>false</c>).
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Timestamp at which the de-identification operation completed.
    /// </summary>
    public DateTimeOffset DeIdentifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of reported ticket records preserved intact in the database.
    /// </summary>
    public int AssociatedTicketsPreserved { get; init; }

    /// <summary>
    /// Number of distinct incident records linked to the user's tickets preserved intact.
    /// </summary>
    public int AssociatedIncidentsPreserved { get; init; }
}

/// <summary>
/// Service contract for executing POPIA-compliant irreversible soft de-identification of user personal information (NFR-05, IT-RET-02).
/// </summary>
public interface IUserDeIdentificationService
{
    /// <summary>
    /// Irreversibly de-identifies an application user:
    /// <list type="bullet">
    ///   <item><description>Replaces <c>full_name</c> with a generic identifier (<c>De-identified User</c>).</description></item>
    ///   <item><description>Replaces <c>email</c> with an anonymized tombstone value (<c>deidentified-{id}@redacted.local</c>).</description></item>
    ///   <item><description>Scrubs <c>password_hash</c> to a permanently invalid sentinel hash (<c>*DEIDENTIFIED*</c>).</description></item>
    ///   <item><description>Sets <c>is_active = false</c>.</description></item>
    ///   <item><description>Closes any active team memberships (<c>left_at = now()</c>).</description></item>
    ///   <item><description>Preserves associated <c>ticket</c> and <c>incident</c> records intact with reference numbers readable.</description></item>
    /// </list>
    /// </summary>
    /// <param name="userId">The unique identifier of the user to de-identify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A summary DTO confirming de-identification and preserved historical links.</returns>
    Task<UserDeIdentificationResultDto> DeIdentifyUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
