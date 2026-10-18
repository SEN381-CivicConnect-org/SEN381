using CivicConnect.Data.Security;
using Npgsql;

namespace CivicConnect.Web.Services;

/// Ensures the demo accounts exist, including two distinct reporters for ownership checks.
public static class DemoDataSeeder
{
    private static readonly IPasswordHashingService Hasher = Argon2idPasswordHasher.Instance;

    private static readonly (string Email, string FullName, string RoleName)[] Accounts =
    {
        ("requester@civicconnect.demo", "Bernard Small", "reporter"),
        ("requester.b@civicconnect.demo", "Ayesha Khan", "reporter"),
        ("staff@civicconnect.demo", "Daniel Jacobs", "technician"),
        ("manager@civicconnect.demo", "Michael Adams", "operations manager")
    };

    private static readonly string[] Teams =
    {
        "IT Support",
        "Facilities"
    };

    public static async Task EnsureDemoUsersAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        foreach (var teamName in Teams)
        {
            await using var ensureTeam = conn.CreateCommand();
            ensureTeam.CommandText =
                """
                INSERT INTO service_team (name)
                VALUES ($1)
                ON CONFLICT (name) DO NOTHING
                """;
            ensureTeam.Parameters.AddWithValue(teamName);
            await ensureTeam.ExecuteNonQueryAsync();
        }

        foreach (var (email, fullName, roleName) in Accounts)
        {
            await using var exists = conn.CreateCommand();
            exists.CommandText = "SELECT password_hash FROM app_user WHERE email = $1";
            exists.Parameters.AddWithValue(email);
            var existingHash = await exists.ExecuteScalarAsync() as string;

            if (existingHash is null)
            {
                await using var insert = conn.CreateCommand();
                insert.CommandText =
                    """
                    WITH new_user AS (
                        INSERT INTO app_user (email, full_name, password_hash)
                        VALUES ($1, $2, $3)
                        RETURNING id
                    )
                    INSERT INTO user_role (user_id, role_id, granted_by_user_id)
                    SELECT new_user.id, role.id, new_user.id
                    FROM new_user, role
                    WHERE role.name = $4
                    """;
                insert.Parameters.AddWithValue(email);
                insert.Parameters.AddWithValue(fullName);
                insert.Parameters.AddWithValue(Hasher.HashPassword("password"));
                insert.Parameters.AddWithValue(roleName);
                await insert.ExecuteNonQueryAsync();
            }
            else if (!Argon2idPasswordHasher.IsValidPhcFormat(existingHash) ||
                     (Hasher.VerifyPassword("password", existingHash, out var needsRehash) && needsRehash))
            {
                await using var update = conn.CreateCommand();
                update.CommandText =
                    """
                    UPDATE app_user
                    SET password_hash = $1, updated_at = now()
                    WHERE email = $2
                    """;
                update.Parameters.AddWithValue(Hasher.HashPassword("password"));
                update.Parameters.AddWithValue(email);
                await update.ExecuteNonQueryAsync();
            }
        }

        await using var ensureStaffTeam = conn.CreateCommand();
        ensureStaffTeam.CommandText =
            """
            INSERT INTO team_member (user_id, service_team_id, team_role_id)
            SELECT u.id, st.id, tr.id
            FROM app_user u
            CROSS JOIN service_team st
            CROSS JOIN team_role tr
            WHERE u.email = 'staff@civicconnect.demo'
              AND st.name = 'IT Support'
              AND tr.name = 'technician'
              AND NOT EXISTS (
                  SELECT 1 FROM team_member tm
                  WHERE tm.user_id = u.id
                    AND tm.service_team_id = st.id
                    AND tm.left_at IS NULL
              )
            """;
        await ensureStaffTeam.ExecuteNonQueryAsync();
    }
}

