using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseRolesAndGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Provision distinct PostgreSQL roles for migration and runtime application (NFR-10, IT-SEC-02)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_migrate') THEN
                        CREATE ROLE civic_migrate WITH LOGIN PASSWORD 'civic_migrate_secure_pass';
                    END IF;
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_app') THEN
                        CREATE ROLE civic_app WITH LOGIN PASSWORD 'civic_app_secure_dev_pass';
                    END IF;
                END
                $$;
            """);

            // 2. Database connect privileges
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_migrate, civic_app', current_database());
                END
                $$;
            """);

            // 3. Schema USAGE and DDL permissions
            migrationBuilder.Sql("""
                GRANT USAGE, CREATE ON SCHEMA public TO civic_migrate;
                GRANT USAGE ON SCHEMA public TO civic_app;

                REVOKE CREATE ON SCHEMA public FROM civic_app;
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            """);

            // 4. civic_migrate full pipeline permissions on all tables, sequences, routines
            migrationBuilder.Sql("""
                GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO civic_migrate;
                GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO civic_migrate;
                GRANT ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA public TO civic_migrate;

                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO civic_migrate;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO civic_migrate;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON ROUTINES TO civic_migrate;
            """);

            // 5. civic_app sequences permissions
            migrationBuilder.Sql("""
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO civic_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;
            """);

            // 6. civic_app operational tables permissions (SELECT, INSERT, UPDATE; strictly NO DELETE)
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON TABLE 
                    app_user, role, user_role, service_team, team_role, team_member,
                    category, priority_target, location_kind, location, asset,
                    incident, ticket, submission_request, incident_subscription,
                    incident_status_transition, resolution_code, resolution, notification
                TO civic_app;

                REVOKE DELETE ON TABLE 
                    app_user, role, user_role, service_team, team_role, team_member,
                    category, priority_target, location_kind, location, asset,
                    incident, ticket, submission_request, incident_subscription,
                    incident_status_transition, resolution_code, resolution, notification
                FROM civic_app;
            """);

            // 7. civic_app append-only audit & history tables (SELECT, INSERT; strictly NO UPDATE, NO DELETE)
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT ON TABLE 
                    audit_log, incident_status_history, incident_note
                TO civic_app;

                REVOKE UPDATE, DELETE ON TABLE 
                    audit_log, incident_status_history, incident_note
                FROM civic_app;
            """);

            // 8. civic_app view & migrations history permissions
            migrationBuilder.Sql("""
                GRANT SELECT ON TABLE staff_directory TO civic_app;
                GRANT SELECT ON TABLE __ef_migrations_history TO civic_app;
            """);

            // 9. Default privileges for future tables
            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;
            """);

            // ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate requires membership in civic_migrate.
            // Managed PostgreSQL (e.g. Render) runs migrations as a non-superuser database owner, so
            // membership is granted temporarily and revoked afterwards unless it was already held.
            migrationBuilder.Sql("""
                DO $defpriv$
                DECLARE
                    was_member boolean := pg_has_role(current_user, 'civic_migrate', 'MEMBER');
                BEGIN
                    IF NOT was_member THEN
                        EXECUTE format('GRANT civic_migrate TO %I', current_user);
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;

                    IF NOT was_member THEN
                        EXECUTE format('REVOKE civic_migrate FROM %I', current_user);
                    END IF;
                END
                $defpriv$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM civic_app, civic_migrate;
                REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public FROM civic_app, civic_migrate;
                REVOKE ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA public FROM civic_app, civic_migrate;
                REVOKE USAGE, CREATE ON SCHEMA public FROM civic_app, civic_migrate;

                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE SELECT, INSERT, UPDATE ON TABLES FROM civic_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE USAGE, SELECT ON SEQUENCES FROM civic_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM civic_migrate;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM civic_migrate;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON ROUTINES FROM civic_migrate;
            """);

            migrationBuilder.Sql("""
                DO $defpriv$
                DECLARE
                    was_member boolean := pg_has_role(current_user, 'civic_migrate', 'MEMBER');
                BEGIN
                    IF NOT was_member THEN
                        EXECUTE format('GRANT civic_migrate TO %I', current_user);
                    END IF;

                    ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public REVOKE SELECT, INSERT, UPDATE ON TABLES FROM civic_app;
                    ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public REVOKE USAGE, SELECT ON SEQUENCES FROM civic_app;

                    IF NOT was_member THEN
                        EXECUTE format('REVOKE civic_migrate FROM %I', current_user);
                    END IF;
                END
                $defpriv$;
            """);
        }
    }
}
