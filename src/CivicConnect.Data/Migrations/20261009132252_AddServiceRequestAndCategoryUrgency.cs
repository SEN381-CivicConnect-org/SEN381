using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRequestAndCategoryUrgency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_urgency",
                table: "category",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "service_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    requester_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Open"),
                    impact = table.Column<int>(type: "integer", nullable: false),
                    urgency = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_supervisor_overridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supervisor_override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_request_app_user_assigned_to_id",
                        column: x => x.assigned_to_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_service_request_app_user_requester_id",
                        column: x => x.requester_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_service_request_category_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_request_assigned_to_id",
                table: "service_request",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_category_id",
                table: "service_request",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_priority",
                table: "service_request",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_requester_id",
                table: "service_request",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_request_status",
                table: "service_request",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_request");

            migrationBuilder.DropColumn(
                name: "default_urgency",
                table: "category");
        }
    }
}
