using System;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentStatusTransitionsAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "assigned_team_id",
                table: "incident",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "incident",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "incident_status_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    from_status = table.Column<IncidentStatus>(type: "incident_status", nullable: true),
                    to_status = table.Column<IncidentStatus>(type: "incident_status", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    note = table.Column<string>(type: "text", nullable: true),
                    assigned_team_id = table.Column<int>(type: "integer", nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_status_history", x => x.id);
                    table.CheckConstraint("incident_status_history_creation_ck", "from_status IS NOT NULL OR to_status = 'NEW'");
                    table.ForeignKey(
                        name: "fk_incident_status_history_app_user_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_status_history_app_user_changed_by",
                        column: x => x.changed_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_status_history_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_status_history_service_teams_assigned_team_id",
                        column: x => x.assigned_team_id,
                        principalTable: "service_team",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incident_status_transition",
                columns: table => new
                {
                    from_status = table.Column<IncidentStatus>(type: "incident_status", nullable: false),
                    to_status = table.Column<IncidentStatus>(type: "incident_status", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_status_transition", x => new { x.from_status, x.to_status });
                });

            migrationBuilder.InsertData(
                table: "incident_status_transition",
                columns: new[] { "from_status", "to_status" },
                values: new object[,]
                {
                    { IncidentStatus.New, IncidentStatus.Assigned },
                    { IncidentStatus.New, IncidentStatus.Rejected },
                    { IncidentStatus.New, IncidentStatus.Merged },
                    { IncidentStatus.Assigned, IncidentStatus.InProgress },
                    { IncidentStatus.Assigned, IncidentStatus.OnHold },
                    { IncidentStatus.Assigned, IncidentStatus.Rejected },
                    { IncidentStatus.Assigned, IncidentStatus.Merged },
                    { IncidentStatus.InProgress, IncidentStatus.OnHold },
                    { IncidentStatus.InProgress, IncidentStatus.Resolved },
                    { IncidentStatus.InProgress, IncidentStatus.Merged },
                    { IncidentStatus.OnHold, IncidentStatus.InProgress },
                    { IncidentStatus.OnHold, IncidentStatus.Resolved },
                    { IncidentStatus.OnHold, IncidentStatus.Merged },
                    { IncidentStatus.Resolved, IncidentStatus.InProgress },
                    { IncidentStatus.Resolved, IncidentStatus.Closed }
                });

            migrationBuilder.CreateIndex(
                name: "ix_incident_assigned_team_id",
                table: "incident",
                column: "assigned_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_assignee_id",
                table: "incident",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_history_assigned_team_id",
                table: "incident_status_history",
                column: "assigned_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_history_assignee_id",
                table: "incident_status_history",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_history_changed_by",
                table: "incident_status_history",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_history_incident_id_changed_at",
                table: "incident_status_history",
                columns: new[] { "incident_id", "changed_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_incident_app_user_assignee_id",
                table: "incident",
                column: "assignee_id",
                principalTable: "app_user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_incident_service_teams_assigned_team_id",
                table: "incident",
                column: "assigned_team_id",
                principalTable: "service_team",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Refuses a row unless it continues from the incident's last status and the from/to pair is permitted.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_status_history_check_transition() RETURNS trigger AS $$
                DECLARE
                    prev_to_status incident_status;
                BEGIN
                    SELECT to_status INTO prev_to_status
                    FROM incident_status_history
                    WHERE incident_id = NEW.incident_id
                    ORDER BY id DESC
                    LIMIT 1;

                    IF prev_to_status IS NULL THEN
                        IF NEW.from_status IS NOT NULL OR NEW.to_status <> 'NEW' THEN
                            RAISE EXCEPTION 'incident %: first status history row must record creation (no from-status, to-status NEW)', NEW.incident_id;
                        END IF;
                    ELSE
                        IF NEW.from_status IS DISTINCT FROM prev_to_status THEN
                            RAISE EXCEPTION 'incident %: from-status % does not match its current status %', NEW.incident_id, NEW.from_status, prev_to_status;
                        END IF;

                        IF NOT EXISTS (
                            SELECT 1 FROM incident_status_transition
                            WHERE from_status = NEW.from_status AND to_status = NEW.to_status
                        ) THEN
                            RAISE EXCEPTION 'incident %: illegal status transition from % to %', NEW.incident_id, NEW.from_status, NEW.to_status;
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_status_history_check_transition_trg
                BEFORE INSERT ON incident_status_history
                FOR EACH ROW
                EXECUTE FUNCTION incident_status_history_check_transition();
            ");

            // History rows can never be changed or removed.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_status_history_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'incident_status_history rows are append-only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_status_history_no_update_trg
                BEFORE UPDATE ON incident_status_history
                FOR EACH ROW
                EXECUTE FUNCTION incident_status_history_append_only();

                CREATE TRIGGER incident_status_history_no_delete_trg
                BEFORE DELETE ON incident_status_history
                FOR EACH ROW
                EXECUTE FUNCTION incident_status_history_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_incident_app_user_assignee_id",
                table: "incident");

            migrationBuilder.DropForeignKey(
                name: "fk_incident_service_teams_assigned_team_id",
                table: "incident");

            // Dropping the table drops its triggers; the trigger functions are standalone and dropped separately.
            migrationBuilder.DropTable(
                name: "incident_status_history");

            migrationBuilder.Sql(@"
                DROP FUNCTION incident_status_history_append_only();
                DROP FUNCTION incident_status_history_check_transition();
            ");

            migrationBuilder.DropTable(
                name: "incident_status_transition");

            migrationBuilder.DropIndex(
                name: "ix_incident_assigned_team_id",
                table: "incident");

            migrationBuilder.DropIndex(
                name: "ix_incident_assignee_id",
                table: "incident");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                table: "incident");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "incident");
        }
    }
}
