using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Stock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderConsumptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Shortfall",
                table: "stock_movements",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "order_consumptions",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_consumptions", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "order_consumption_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_consumption_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_consumption_lines_order_consumptions_OrderId",
                        column: x => x.OrderId,
                        principalTable: "order_consumptions",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_consumption_lines_OrderId",
                table: "order_consumption_lines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_consumptions_TenantId",
                table: "order_consumptions",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_consumption_lines");

            migrationBuilder.DropTable(
                name: "order_consumptions");

            migrationBuilder.DropColumn(
                name: "Shortfall",
                table: "stock_movements");
        }
    }
}
