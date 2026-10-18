using System;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_action", "CREATE,UPDATE,DEACTIVATE,OVERRIDE")
                .Annotation("Npgsql:Enum:audit_entity_type", "USER,ROLE_GRANT,CATEGORY,LOCATION,ASSET,INCIDENT")
                .Annotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .Annotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .OldAnnotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<AuditEntityType>(type: "audit_entity_type", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<AuditAction>(type: "audit_action", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: true),
                    incident_id = table.Column<long>(type: "bigint", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_app_user_actor_id",
                        column: x => x.actor_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_log_incidents_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_id",
                table: "audit_log",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_incident_id_occurred_at",
                table: "audit_log",
                columns: new[] { "incident_id", "occurred_at" },
                filter: "incident_id IS NOT NULL");

            // Entries can never be changed or removed.
            migrationBuilder.Sql(@"
                CREATE FUNCTION audit_log_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log rows are append-only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_log_no_update_trg
                BEFORE UPDATE ON audit_log
                FOR EACH ROW
                EXECUTE FUNCTION audit_log_append_only();

                CREATE TRIGGER audit_log_no_delete_trg
                BEFORE DELETE ON audit_log
                FOR EACH ROW
                EXECUTE FUNCTION audit_log_append_only();
            ");

            // Configuration-row auditing (category/location/asset/app_user/user_role) is deferred:
            // those tables carry no actor column, and there is no application write path yet to
            // supply one. Wire it up alongside whichever ticket adds their CRUD.
            migrationBuilder.Sql(@"
                CREATE FUNCTION audit_incident_priority_override() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO audit_log (actor_id, entity_type, entity_id, incident_id, action, before_state, after_state)
                    VALUES (
                        NEW.override_by,
                        'INCIDENT',
                        NEW.id::text,
                        NEW.id,
                        'OVERRIDE',
                        jsonb_build_object('priority', OLD.priority, 'priority_overridden', OLD.priority_overridden),
                        jsonb_build_object(
                            'priority', NEW.priority,
                            'priority_overridden', NEW.priority_overridden,
                            'override_reason', NEW.override_reason,
                            'override_by', NEW.override_by,
                            'override_at', NEW.override_at)
                    );

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_priority_override_audit_trg
                AFTER UPDATE ON incident
                FOR EACH ROW
                WHEN (NEW.priority_overridden AND NEW.override_at IS DISTINCT FROM OLD.override_at)
                EXECUTE FUNCTION audit_incident_priority_override();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER incident_priority_override_audit_trg ON incident;
                DROP FUNCTION audit_incident_priority_override();
            ");

            // Dropping the table drops its own triggers; the append-only function is standalone.
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.Sql(@"
                DROP FUNCTION audit_log_append_only();
            ");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .Annotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:audit_action", "CREATE,UPDATE,DEACTIVATE,OVERRIDE")
                .OldAnnotation("Npgsql:Enum:audit_entity_type", "USER,ROLE_GRANT,CATEGORY,LOCATION,ASSET,INCIDENT")
                .OldAnnotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .OldAnnotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
