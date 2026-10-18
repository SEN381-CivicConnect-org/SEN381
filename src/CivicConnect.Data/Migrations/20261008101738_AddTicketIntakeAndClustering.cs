using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketIntakeAndClustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incident_subscription",
                columns: table => new
                {
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscribed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident_subscription", x => new { x.incident_id, x.reporter_id });
                    table.ForeignKey(
                        name: "fk_incident_subscription_app_user_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_subscription_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference_number = table.Column<string>(type: "text", nullable: false, computedColumnSql: "'TKT-' || lpad(id::text, 6, '0')", stored: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_app_user_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_category_category_id",
                        column: x => x.category_id,
                        principalTable: "category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_incident_incident_id",
                        column: x => x.incident_id,
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_location_location_id",
                        column: x => x.location_id,
                        principalTable: "location",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_request",
                columns: table => new
                {
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_fingerprint = table.Column<string>(type: "text", nullable: false),
                    ticket_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_request", x => new { x.reporter_id, x.idempotency_key });
                    table.ForeignKey(
                        name: "fk_submission_request_app_user_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_request_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "ticket",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_incident_subscription_reporter_id",
                table: "incident_subscription",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_request_expires_at",
                table: "submission_request",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_submission_request_ticket_id",
                table: "submission_request",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_category_id",
                table: "ticket",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_incident_id",
                table: "ticket",
                column: "incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_location_id",
                table: "ticket",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_reference_number",
                table: "ticket",
                column: "reference_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_reporter_id",
                table: "ticket",
                column: "reporter_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incident_subscription");

            migrationBuilder.DropTable(
                name: "submission_request");

            migrationBuilder.DropTable(
                name: "ticket");
        }
    }
}
