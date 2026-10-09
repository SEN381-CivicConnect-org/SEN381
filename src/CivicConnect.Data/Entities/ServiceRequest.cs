using CivicConnect.Data.Entities;

namespace CivicConnect.Domain.Entities;

/// <summary>
/// Core domain entity for a community service request in the CivicConnect platform.
/// Encapsulates business logic, ownership lifecycle, and status transitions (FR-04).
/// </summary>
public class ServiceRequest : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? RequesterId { get; set; }
    public AppUser? Requester { get; set; }

    /// <summary>
    /// Identifier of the assigned staff member, matching the system's identity configuration (AppUser.Id).
    /// </summary>
    public Guid? AssignedStaffId { get; private set; }

    public AppUser? AssignedStaff { get; set; }

    public string Status { get; private set; } = "Open";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Records the timestamp of the last modification to the service request entity.
    /// </summary>
    public DateTimeOffset LastModified { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Rich domain method to claim ownership of an unassigned request (FR-04).
    /// Enforces business invariants: validates the staff identifier and ensures the ticket is not already claimed.
    /// </summary>
    /// <param name="staffId">The string identifier extracted from user claims.</param>
    public void Claim(string staffId)
    {
        if (string.IsNullOrWhiteSpace(staffId))
        {
            throw new ArgumentException("Staff ID cannot be null or whitespace.", nameof(staffId));
        }

        if (!Guid.TryParse(staffId, out var parsedStaffId) || parsedStaffId == Guid.Empty)
        {
            throw new ArgumentException("Staff ID must be a valid, non-empty GUID matching the system identity configuration.", nameof(staffId));
        }

        Claim(parsedStaffId);
    }

    /// <summary>
    /// Strongly-typed overload to claim ownership using a Guid staff identifier.
    /// </summary>
    /// <param name="staffId">The GUID of the claiming staff member.</param>
    public void Claim(Guid staffId)
    {
        if (staffId == Guid.Empty)
        {
            throw new ArgumentException("Staff ID cannot be an empty GUID.", nameof(staffId));
        }

        if (AssignedStaffId.HasValue && AssignedStaffId.Value != Guid.Empty)
        {
            throw new InvalidOperationException($"Service request '{Id}' has already been claimed or assigned to staff member '{AssignedStaffId.Value}'.");
        }

        AssignedStaffId = staffId;
        Status = "Assigned";
        LastModified = DateTimeOffset.UtcNow;
        UpdatedAt = LastModified;
    }

    /// <summary>
    /// Allows supervisors to explicitly assign or reassign ownership (FR-04).
    /// </summary>
    /// <param name="staffId">The target staff member GUID, or null to unassign.</param>
    public void AssignTo(Guid? staffId)
    {
        AssignedStaffId = staffId;
        Status = staffId.HasValue ? "Assigned" : "Open";
        LastModified = DateTimeOffset.UtcNow;
        UpdatedAt = LastModified;
    }
}
