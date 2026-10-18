using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Entities;

/// <summary>
/// Keyless projection mapped to the <c>staff_directory</c> database view (NFR-04, IT-SEC-01).
/// Exposes safe staff directory fields while strictly omitting <c>password_hash</c>, email, and personal contact details.
/// </summary>
[Keyless]
public sealed class StaffDirectory
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public int? ServiceTeamId { get; set; }
    public string? TeamName { get; set; }
    public short? TeamRoleId { get; set; }
    public string? TeamRole { get; set; }
    public bool IsActive { get; set; }

    [NotMapped]
    public Guid UserId => Id;
}
