using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManagementReadPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_incident_due_at_open",
                table: "incident",
                column: "due_at",
                filter: "status IN ('NEW', 'ASSIGNED', 'IN_PROGRESS', 'ON_HOLD')");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status",
                table: "incident",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_assigned_team_id",
                table: "incident",
                columns: new[] { "status", "assigned_team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_status_category_id",
                table: "incident",
                columns: new[] { "status", "category_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_incident_due_at_open",
                table: "incident");

            migrationBuilder.DropIndex(
                name: "ix_incident_status",
                table: "incident");

            migrationBuilder.DropIndex(
                name: "ix_incident_status_assigned_team_id",
                table: "incident");

            migrationBuilder.DropIndex(
                name: "ix_incident_status_category_id",
                table: "incident");
        }
    }
}
