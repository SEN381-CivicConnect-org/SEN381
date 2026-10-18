using System;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .Annotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "incident_note",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_note", x => x.id);
                    table.ForeignKey(
                        name: "fk_incident_note_app_user_author_id",
                        column: x => x.author_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_note_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<NotificationKind>(type: "notification_kind", nullable: false),
                    dedup_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_app_user_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incident_note_author_id",
                table: "incident_note",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_incident_note_incident_id_created_at",
                table: "incident_note",
                columns: new[] { "incident_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_incident_id",
                table: "notification",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipient_unread",
                table: "notification",
                column: "recipient_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "notification_dedup_key_uq",
                table: "notification",
                column: "dedup_key",
                unique: true);

            // Notes can never be changed or removed.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_note_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'incident_note rows are append-only';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_note_no_update_trg
                BEFORE UPDATE ON incident_note
                FOR EACH ROW
                EXECUTE FUNCTION incident_note_append_only();

                CREATE TRIGGER incident_note_no_delete_trg
                BEFORE DELETE ON incident_note
                FOR EACH ROW
                EXECUTE FUNCTION incident_note_append_only();
            ");

            // Fans a status transition out to one notification per subscriber; assignment gets its own kind.
            migrationBuilder.Sql(@"
                CREATE FUNCTION incident_status_history_notify() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO notification (incident_id, recipient_id, kind, dedup_key)
                    SELECT
                        NEW.incident_id,
                        s.reporter_id,
                        CASE WHEN NEW.to_status = 'ASSIGNED' THEN 'ASSIGNMENT' ELSE 'STATUS_CHANGE' END::notification_kind,
                        'status:' || NEW.id::text || ':' || s.reporter_id::text
                    FROM incident_subscription s
                    WHERE s.incident_id = NEW.incident_id
                    ON CONFLICT (dedup_key) DO NOTHING;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER incident_status_history_notify_trg
                AFTER INSERT ON incident_status_history
                FOR EACH ROW
                EXECUTE FUNCTION incident_status_history_notify();
            ");

            // Fans a resolution out to one notification per subscriber.
            migrationBuilder.Sql(@"
                CREATE FUNCTION resolution_notify() RETURNS trigger AS $$
                BEGIN
                    INSERT INTO notification (incident_id, recipient_id, kind, dedup_key)
                    SELECT
                        NEW.incident_id,
                        s.reporter_id,
                        'RESOLUTION'::notification_kind,
                        'resolution:' || NEW.id::text || ':' || s.reporter_id::text
                    FROM incident_subscription s
                    WHERE s.incident_id = NEW.incident_id
                    ON CONFLICT (dedup_key) DO NOTHING;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER resolution_notify_trg
                AFTER INSERT ON resolution
                FOR EACH ROW
                EXECUTE FUNCTION resolution_notify();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // These triggers insert into notification, so they must go before the table does.
            migrationBuilder.Sql(@"
                DROP TRIGGER incident_status_history_notify_trg ON incident_status_history;
                DROP FUNCTION incident_status_history_notify();

                DROP TRIGGER resolution_notify_trg ON resolution;
                DROP FUNCTION resolution_notify();
            ");

            migrationBuilder.DropTable(
                name: "notification");

            // Dropping the table drops its triggers; the trigger function is standalone and dropped separately.
            migrationBuilder.DropTable(
                name: "incident_note");

            migrationBuilder.Sql(@"
                DROP FUNCTION incident_note_append_only();
            ");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:incident_status", "NEW,ASSIGNED,IN_PROGRESS,ON_HOLD,RESOLVED,CLOSED,REJECTED,MERGED")
                .OldAnnotation("Npgsql:Enum:notification_kind", "STATUS_CHANGE,ASSIGNMENT,RESOLUTION")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
