using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResolutionAndResolutionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resolution_code",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    implies_replacement = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resolution_code", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resolution",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    resolution_code_id = table.Column<short>(type: "smallint", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    asset_id = table.Column<int>(type: "integer", nullable: true),
                    needs_replacement = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resolution", x => x.id);
                    table.ForeignKey(
                        name: "fk_resolution_app_user_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resolution_asset_asset_id",
                        column: x => x.asset_id,
                        principalTable: "asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resolution_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resolution_resolution_code_resolution_code_id",
                        column: x => x.resolution_code_id,
                        principalTable: "resolution_code",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "resolution_code",
                columns: new[] { "id", "code", "created_at", "implies_replacement", "is_active", "name" },
                values: new object[,]
                {
                    { (short)1, "FIXED", new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, true, "Fixed on site" },
                    { (short)2, "NO_FAULT_FOUND", new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, true, "No fault found" },
                    { (short)3, "DUPLICATE", new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, true, "Duplicate of another incident" },
                    { (short)4, "HARDWARE_REPLACED", new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Faulty hardware replaced" },
                    { (short)5, "BEYOND_REPAIR", new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "Beyond repair, replacement required" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_resolution_asset_id",
                table: "resolution",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_resolution_incident_id_resolved_at",
                table: "resolution",
                columns: new[] { "incident_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_resolution_resolution_code_id",
                table: "resolution",
                column: "resolution_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_resolution_resolved_by",
                table: "resolution",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "resolution_incident_active_uq",
                table: "resolution",
                column: "incident_id",
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_resolution_code_code",
                table: "resolution_code",
                column: "code",
                unique: true);

            // Enforces the replacement-code/asset rule, derives needs_replacement, and supersedes
            // the incident's prior active resolution before the new row lands.
            migrationBuilder.Sql(@"
                CREATE FUNCTION resolution_before_insert() RETURNS trigger AS $$
                DECLARE
                    code_implies_replacement boolean;
                BEGIN
                    SELECT implies_replacement INTO code_implies_replacement
                    FROM resolution_code
                    WHERE id = NEW.resolution_code_id;

                    IF code_implies_replacement AND NEW.asset_id IS NULL THEN
                        RAISE EXCEPTION 'resolution code %: implies replacement and requires an asset link', NEW.resolution_code_id;
                    END IF;

                    NEW.needs_replacement := code_implies_replacement;

                    UPDATE resolution
                    SET superseded_at = now()
                    WHERE incident_id = NEW.incident_id AND superseded_at IS NULL;

                    UPDATE incident
                    SET status = 'RESOLVED', resolved_at = now()
                    WHERE id = NEW.incident_id;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER resolution_before_insert_trg
                BEFORE INSERT ON resolution
                FOR EACH ROW
                EXECUTE FUNCTION resolution_before_insert();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table drops its trigger; the trigger function is standalone and dropped separately.
            migrationBuilder.DropTable(
                name: "resolution");

            migrationBuilder.Sql(@"
                DROP FUNCTION resolution_before_insert();
            ");

            migrationBuilder.DropTable(
                name: "resolution_code");
        }
    }
}
