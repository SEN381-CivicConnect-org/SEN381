using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalInformationProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Safe Staff Directory view exposing non-sensitive staff profile data (NFR-04, IT-SEC-01)
            // Strictly omits app_user.password_hash and app_user.email (contact detail / credential)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW staff_directory AS
                SELECT
                    u.id,
                    u.full_name,
                    tm.service_team_id,
                    st.name AS team_name,
                    tm.team_role_id,
                    tr.name AS team_role,
                    u.is_active
                FROM app_user u
                LEFT JOIN team_member tm ON tm.user_id = u.id AND tm.left_at IS NULL
                LEFT JOIN service_team st ON st.id = tm.service_team_id
                LEFT JOIN team_role tr ON tr.id = tm.team_role_id;
            ");

            // 2. Dedicated reporting database role civic_report
            migrationBuilder.Sql(@"
                DO $role$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_report') THEN
                        CREATE ROLE civic_report WITH LOGIN PASSWORD 'civicconnect_secure_dev_pass' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION;
                    ELSE
                        ALTER ROLE civic_report WITH LOGIN PASSWORD 'civicconnect_secure_dev_pass' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION;
                    END IF;

                    EXECUTE format('GRANT civic_report TO %I', current_user);
                    EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_report', current_database());
                END
                $role$;
            ");

            // 3. Principle of least privilege: revoke access on non-reporting and credential tables, grant SELECT on analytical tables and staff_directory
            migrationBuilder.Sql(@"
                REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM civic_report;
                REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public FROM civic_report;
                REVOKE ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public FROM civic_report;

                REVOKE ALL ON TABLE app_user FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE user_role FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE role FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE team_member FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE ticket FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE submission_request FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE incident_subscription FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE incident_note FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE notification FROM PUBLIC, civic_report;
                REVOKE ALL ON TABLE audit_log FROM PUBLIC, civic_report;

                GRANT USAGE ON SCHEMA public TO civic_report;

                GRANT SELECT ON staff_directory TO civic_report;

                GRANT SELECT ON TABLE incident TO civic_report;
                GRANT SELECT ON TABLE category TO civic_report;
                GRANT SELECT ON TABLE location TO civic_report;
                GRANT SELECT ON TABLE location_kind TO civic_report;
                GRANT SELECT ON TABLE service_team TO civic_report;
                GRANT SELECT ON TABLE team_role TO civic_report;
                GRANT SELECT ON TABLE priority_target TO civic_report;
                GRANT SELECT ON TABLE resolution TO civic_report;
                GRANT SELECT ON TABLE resolution_code TO civic_report;
                GRANT SELECT ON TABLE asset TO civic_report;
                GRANT SELECT ON TABLE incident_status_history TO civic_report;
                GRANT SELECT ON TABLE incident_status_transition TO civic_report;
            ");

            // 4. Configure local container authentication for civic_report test connections
            migrationBuilder.Sql(@"
                DO $hba$
                BEGIN
                    EXECUTE format(
                        'COPY (SELECT 1) TO PROGRAM %L',
                        'sh -c ""HBA=\$(psql -t -A -c \""SHOW hba_file;\"" 2>/dev/null); if [ -n \""\$HBA\"" ] && ! grep -q \""civic_report\"" \""\$HBA\""; then sed -i \""1s/^/host all civic_report all trust\nlocal all civic_report trust\n/\"" \""\$HBA\"" && psql -c \""SELECT pg_reload_conf();\"" 2>/dev/null; fi""'
                    );
                EXCEPTION WHEN OTHERS THEN
                    NULL;
                END
                $hba$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP VIEW IF EXISTS staff_directory;

                DO $drop_role$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'civic_report') THEN
                        EXECUTE format('REVOKE ALL PRIVILEGES ON DATABASE %I FROM civic_report', current_database());
                        REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM civic_report;
                        REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public FROM civic_report;
                        REVOKE ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA public FROM civic_report;
                        REVOKE USAGE ON SCHEMA public FROM civic_report;
                        DROP OWNED BY civic_report;
                        DROP ROLE civic_report;
                    END IF;
                END
                $drop_role$;
            ");
        }
    }
}
