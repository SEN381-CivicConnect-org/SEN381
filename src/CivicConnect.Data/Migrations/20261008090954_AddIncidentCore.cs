using System;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_category_priority_targets_priority_target_id",
                table: "category");

            migrationBuilder.DropIndex(
                name: "ix_category_priority_target_id",
                table: "category");

            // Urgency level is a new, independently-scaled input (used together with location.impact_level to
            // compute an incident's priority), not a renamed priority_target_id; category has no rows yet.
            migrationBuilder.DropColumn(
                name: "priority_target_id",
                table: "category");

            migrationBuilder.AddColumn<short>(
                name: "urgency_level",
                table: "category",
                type: "smallint",
                nullable: false);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "incident",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference_number = table.Column<string>(type: "text", nullable: false, computedColumnSql: "'INC-' || lpad(id::text, 6, '0')", stored: true),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<IncidentStatus>(type: "incident_status", nullable: false, defaultValue: IncidentStatus.New),
                    computed_priority = table.Column<short>(type: "smallint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false),
                    priority_overridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    override_reason = table.Column<string>(type: "text", nullable: true),
                    override_by = table.Column<Guid>(type: "uuid", nullable: true),
                    override_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_into_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "integer", rowVersion: true, nullable: false, defaultValue: 1),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident", x => x.id);
                    table.CheckConstraint("incident_merge_not_self_ck", "id <> merged_into_id");
                    table.CheckConstraint("incident_override_ck", "(NOT priority_overridden\n                      AND override_reason IS NULL AND override_by IS NULL AND override_at IS NULL\n                      AND priority = computed_priority)\n                  OR (priority_overridden\n                      AND override_reason IS NOT NULL AND btrim(override_reason) <> ''\n                      AND override_by IS NOT NULL AND override_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_incident_app_user_override_by",
                        column: x => x.override_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_category_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_incident_merged_into_id",
                        column: x => x.merged_into_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_priority_targets_computed_priority",
                        column: x => x.computed_priority,
                        principalTable: "priority_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_priority_targets_priority",
                        column: x => x.priority,
                        principalTable: "priority_target",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "category_urgency_level_range_ck",
                table: "category",
                sql: "urgency_level BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "ix_incident_category_id",
                table: "incident",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_computed_priority",
                table: "incident",
                column: "computed_priority");

            migrationBuilder.CreateIndex(
                name: "ix_incident_location_id",
                table: "incident",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_merged_into_id",
                table: "incident",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_override_by",
                table: "incident",
                column: "override_by");

            migrationBuilder.CreateIndex(
                name: "ix_incident_priority",
                table: "incident",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_incident_reference_number",
                table: "incident",
                column: "reference_number",
                unique: true);

            // Version invariant. Bumped on every update so a stale-version WHERE clause
            // (EF's optimistic concurrency check) detects a concurrent edit.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_bump_version() RETURNS trigger AS $$
                BEGIN
                    NEW.version := OLD.version + 1;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_version_bump_trg
                BEFORE UPDATE ON incident
                FOR EACH ROW
                EXECUTE FUNCTION incident_bump_version();
            ");

            // Due-date invariant. due_at always reflects the effective priority's resolve-within-minutes
            // value, anchored to opened_at, whether set at creation or recalculated after an override.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_set_due_at() RETURNS trigger AS $$
                BEGIN
                    NEW.due_at := NEW.opened_at
                        + (SELECT resolve_within_minutes FROM priority_target WHERE id = NEW.priority) * INTERVAL '1 minute';
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_due_at_insert_trg
                BEFORE INSERT ON incident
                FOR EACH ROW
                EXECUTE FUNCTION incident_set_due_at();

                CREATE TRIGGER incident_due_at_update_trg
                BEFORE UPDATE OF priority ON incident
                FOR EACH ROW
                EXECUTE FUNCTION incident_set_due_at();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table drops its triggers; the trigger functions are standalone and dropped separately.
            migrationBuilder.DropTable(
                name: "incident");

            migrationBuilder.Sql(@"
                DROP FUNCTION incident_set_due_at();
                DROP FUNCTION incident_bump_version();
            ");

            migrationBuilder.DropCheckConstraint(
                name: "category_urgency_level_range_ck",
                table: "category");

            migrationBuilder.DropColumn(
                name: "urgency_level",
                table: "category");

            migrationBuilder.AddColumn<short>(
                name: "priority_target_id",
                table: "category",
                type: "smallint",
                nullable: false);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_category_priority_target_id",
                table: "category",
                column: "priority_target_id");

            migrationBuilder.AddForeignKey(
                name: "fk_category_priority_targets_priority_target_id",
                table: "category",
                column: "priority_target_id",
                principalTable: "priority_target",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
