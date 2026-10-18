using CivicConnect.Data.Security;
using Npgsql;

namespace CivicConnect.Web.Services;

/// Ensures the demo accounts and baseline service categories exist for workspace operations.
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

    private static readonly (string Name, string Description, short UrgencyLevel, string DefaultTeamName)[] DefaultCategories =
    {
        ("IT Support", "Workstation, network, connectivity, and software access incidents.", 3, "IT Support"),
        ("Facilities", "Building maintenance, HVAC, plumbing, and room equipment requests.", 2, "Facilities"),
        ("Electrical", "Power faults, lighting outages, and electrical safety hazards.", 4, "Facilities"),
        ("Safety Incident", "Urgent physical safety, access control, or workplace hazard reports.", 4, "Facilities"),
        ("HR Request", "Human resources policy, onboarding, and staff administration requests.", 1, "IT Support")
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

        foreach (var (name, description, urgencyLevel, defaultTeamName) in DefaultCategories)
        {
            await using var ensureCategory = conn.CreateCommand();
            ensureCategory.CommandText =
                """
                INSERT INTO category (name, description, urgency_level, default_service_team_id, is_active)
                SELECT $1, $2, $3, st.id, true
                FROM service_team st
                WHERE st.name = $4
                  AND NOT EXISTS (
                      SELECT 1 FROM category c
                      WHERE c.name = $1 AND c.is_active = true
                  )
                """;
            ensureCategory.Parameters.AddWithValue(name);
            ensureCategory.Parameters.AddWithValue(description);
            ensureCategory.Parameters.AddWithValue(urgencyLevel);
            ensureCategory.Parameters.AddWithValue(defaultTeamName);
            await ensureCategory.ExecuteNonQueryAsync();
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
