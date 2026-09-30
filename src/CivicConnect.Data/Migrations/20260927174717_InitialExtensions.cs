using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS citext;");
        }
    }
}
