using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data;

/// One step in a location's ancestor path, root first.
public sealed record LocationPathEntry(int Id, string Name, short LocationKindId, int Depth);

/// Reads a location's full ancestor path by walking parent_location_id to the root.
public static class LocationAncestry
{
    private const string Sql = """
        WITH RECURSIVE ancestors AS (
            SELECT id, name, location_kind_id, parent_location_id, 0 AS depth
            FROM location
            WHERE id = @locationId

            UNION ALL

            SELECT l.id, l.name, l.location_kind_id, l.parent_location_id, a.depth + 1
            FROM location l
            JOIN ancestors a ON l.id = a.parent_location_id
        )
        SELECT id, name, location_kind_id, depth
        FROM ancestors
        ORDER BY depth DESC
        """;

    public static async Task<IReadOnlyList<LocationPathEntry>> GetAncestorPathAsync(
        this CivicConnectDbContext db,
        int locationId,
        CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "locationId";
            parameter.Value = locationId;
            command.Parameters.Add(parameter);

            var path = new List<LocationPathEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                path.Add(new LocationPathEntry(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt16(2),
                    reader.GetInt32(3)));
            }

            return path;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
