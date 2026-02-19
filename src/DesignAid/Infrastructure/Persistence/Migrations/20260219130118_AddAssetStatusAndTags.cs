using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesignAid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetStatusAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Assets",
                type: "TEXT",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Assets",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Assets");
        }
    }
}
