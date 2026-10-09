using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace CivicConnect.Web.Services;

/// Ensures the three demo accounts (one per portal) exist so the login screen has something to sign into.
public static class DemoDataSeeder
{
    private static readonly PasswordHasher<object> Hasher = new();

    private static readonly (string Email, string FullName, string RoleName)[] Accounts =
    {
        ("requester@civicconnect.demo", "Bernard Small", "reporter"),
        ("staff@civicconnect.demo", "Daniel Jacobs", "technician"),
        ("manager@civicconnect.demo", "Michael Adams", "operations manager")
    };

    public static async Task EnsureDemoUsersAsync(NpgsqlDataSource dataSource)
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        foreach (var (email, fullName, roleName) in Accounts)
        {
            await using var exists = conn.CreateCommand();
            exists.CommandText = "SELECT 1 FROM app_user WHERE email = $1";
            exists.Parameters.AddWithValue(email);
            if (await exists.ExecuteScalarAsync() is not null) continue;

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
            insert.Parameters.AddWithValue(Hasher.HashPassword(null!, "password"));
            insert.Parameters.AddWithValue(roleName);
            await insert.ExecuteNonQueryAsync();
        }
    }
}
