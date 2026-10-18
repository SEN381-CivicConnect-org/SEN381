using System;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentTeamQueueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "incident_team_queue_idx",
                table: "incident",
                columns: new[] { "assigned_team_id", "opened_at" },
                descending: new[] { false, true },
                filter: "status IN ('NEW', 'ASSIGNED', 'IN_PROGRESS', 'ON_HOLD')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "incident_team_queue_idx",
                table: "incident");
        }
    }
}
