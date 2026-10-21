using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockItemXminConcurrency : Migration
    {
        // xmin is a PostgreSQL system column present on every table: the concurrency
        // token needs no schema change (EF scaffolds an AddColumn that would fail).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
