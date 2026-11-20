using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CancelPerOriginalUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_reference_transaction_id",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "ux_transactions_cancel_reference",
                table: "transactions",
                column: "reference_transaction_id",
                unique: true,
                filter: "type = 'Cancel'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_transactions_cancel_reference",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_reference_transaction_id",
                table: "transactions",
                column: "reference_transaction_id");
        }
    }
}
