using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationHierarchyAndAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "location_kind",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_kind", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "location",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    impact_level = table.Column<short>(type: "smallint", nullable: false),
                    location_kind_id = table.Column<short>(type: "smallint", nullable: false),
                    parent_location_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location", x => x.id);
                    table.CheckConstraint("location_impact_level_range_ck", "impact_level BETWEEN 1 AND 4");
                    table.CheckConstraint("location_parent_not_self_ck", "id <> parent_location_id");
                    table.ForeignKey(
                        name: "fk_location_location_kinds_location_kind_id",
                        column: x => x.location_kind_id,
                        principalTable: "location_kind",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_location_parent_location_id",
                        column: x => x.parent_location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_tag = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    supplier = table.Column<string>(type: "text", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    location_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "location_kind",
                columns: new[] { "id", "created_at", "name" },
                values: new object[,]
                {
                    { (short)1, new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "site" },
                    { (short)2, new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "floor" },
                    { (short)3, new DateTimeOffset(new DateTime(2026, 9, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "room" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_asset_tag",
                table: "asset",
                column: "asset_tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_location_id",
                table: "asset",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_location_kind_id",
                table: "location",
                column: "location_kind_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_parent_location_id",
                table: "location",
                column: "parent_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_kind_name",
                table: "location_kind",
                column: "name",
                unique: true);

            // Direct self-parenting is caught by location_parent_not_self_ck; this catches a parent
            // set to one of the location's own descendants, which no row-local constraint can see.
            migrationBuilder.Sql("""
                CREATE FUNCTION location_prevent_cycle() RETURNS trigger AS $$
                BEGIN
                    IF NEW.parent_location_id IS NOT NULL AND EXISTS (
                        WITH RECURSIVE ancestors AS (
                            SELECT parent_location_id FROM location WHERE id = NEW.parent_location_id
                            UNION ALL
                            SELECT l.parent_location_id
                            FROM location l
                            JOIN ancestors a ON l.id = a.parent_location_id
                        )
                        SELECT 1 FROM ancestors WHERE parent_location_id = NEW.id
                    ) THEN
                        RAISE EXCEPTION 'location % cannot be an ancestor of itself', NEW.id;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER location_prevent_cycle_trigger
                BEFORE INSERT OR UPDATE OF parent_location_id ON location
                FOR EACH ROW
                EXECUTE FUNCTION location_prevent_cycle();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER location_prevent_cycle_trigger ON location;");
            migrationBuilder.Sql("DROP FUNCTION location_prevent_cycle();");

            migrationBuilder.DropTable(
                name: "asset");

            migrationBuilder.DropTable(
                name: "location");

            migrationBuilder.DropTable(
                name: "location_kind");
        }
    }
}
