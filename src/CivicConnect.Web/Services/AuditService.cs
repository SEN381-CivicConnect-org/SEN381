using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CivicConnect.Web.Models;

namespace CivicConnect.Web.Services;

/// Append-only audit for the current in-memory ticket workspace.
public sealed class AuditService
{
    private readonly List<AuditEntry> entries = new();
    private readonly object auditLock = new();
    private static readonly string[] SensitiveKeys =
    [
        "password", "email", "phone", "mobile", "contact", "address",
        "token", "secret", "credential", "authorization", "cookie"
    ];

    public static string ActorFor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("An authenticated actor is required.");
        var actor = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub")
            ?? user.Identity.Name ?? user.FindFirstValue(ClaimTypes.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        // Preserve attribution without storing email addresses or phone numbers.
        return SafeIdentifier(actor);
    }

    public void Record(
        string actor,
        string entityType,
        string entityId,
        string action,
        IReadOnlyDictionary<string, object?>? previousValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        // Sanitize before storing; callers never append raw entries or supply timestamps.
        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            Actor = SafeIdentifier(actor),
            EntityType = RedactContact(entityType),
            EntityId = SafeIdentifier(entityId),
            Action = RedactContact(action),
            PreviousValues = Sanitize(previousValues),
            NewValues = Sanitize(newValues)
        };
        lock (auditLock)
        {
            entries.Add(entry);
        }
    }

    public IReadOnlyList<AuditEntry> GetForEntity(string entityType, string entityId)
    {
        lock (auditLock)
        {
            return entries.Where(e => e.EntityType == RedactContact(entityType) && e.EntityId == SafeIdentifier(entityId)).ToArray();
        }
    }

    private static string? Sanitize(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(values)!;
        Filter(node);
        return node.ToJsonString();
    }

    private static void Filter(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (SensitiveKeys.Any(normalized.Contains))
                {
                    obj.Remove(key);
                }
                else if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    // Only known workflow labels may contain strings. Arbitrary free text can
                    // contain credentials even when its field name looks harmless.
                    if (IsWorkflowLabel(key, text))
                        obj[key] = text;
                    else
                        obj.Remove(key);
                }
                else if (obj[key] is { } child)
                {
                    Filter(child);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    array[i] = "[redacted]";
                }
                else if (array[i] is { } child)
                {
                    Filter(child);
                }
            }
        }
    }

    private static bool IsWorkflowLabel(string key, string value) => key switch
    {
        "Priority" => value is "Low" or "Medium" or "High" or "Critical",
        "Status" => value is "Open" or "New" or "Assigned" or "In Progress" or "Resolved" or "Closed"
            or "NEW" or "ASSIGNED" or "IN_PROGRESS" or "RESOLVED" or "CLOSED",
        "Role" => value is "Requester" or "Staff" or "Management"
            or "Reporter" or "Technician" or "Supervisor" or "Operations Manager" or "Administrator"
            or "reporter" or "technician" or "supervisor" or "operations manager" or "administrator",
        _ => false
    };

    private static string SafeIdentifier(string value) =>
        Guid.TryParse(value, out _) || RedactContact(value) == value ? value
            : "User:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RedactContact(string value)
    {
        var withoutEmails = Regex.Replace(value, @"[^\s<>]+@[^\s<>]+", "[redacted]");
        return Regex.Replace(withoutEmails, @"(?<!\w)\+?\d[\d ()-]{5,}\d(?!\w)", "[redacted]");
    }
}
