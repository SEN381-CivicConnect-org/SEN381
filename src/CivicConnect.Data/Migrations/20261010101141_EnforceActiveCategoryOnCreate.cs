using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicConnect.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceActiveCategoryOnCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A retired category can no longer be picked for a newly-opened ticket or incident, but
            // existing rows that already reference it are untouched (no cascade, no re-check on update).
            migrationBuilder.Sql(@"
                CREATE FUNCTION category_must_be_active_on_insert() RETURNS trigger AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM category WHERE id = NEW.category_id AND is_active) THEN
                        RAISE EXCEPTION 'category % is not active and cannot be selected for a new %', NEW.category_id, TG_TABLE_NAME;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER ticket_category_active_insert_trg
                BEFORE INSERT ON ticket
                FOR EACH ROW
                EXECUTE FUNCTION category_must_be_active_on_insert();

                CREATE TRIGGER incident_category_active_insert_trg
                BEFORE INSERT ON incident
                FOR EACH ROW
                EXECUTE FUNCTION category_must_be_active_on_insert();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER incident_category_active_insert_trg ON incident;
                DROP TRIGGER ticket_category_active_insert_trg ON ticket;
                DROP FUNCTION category_must_be_active_on_insert();
            ");
        }
    }
}
