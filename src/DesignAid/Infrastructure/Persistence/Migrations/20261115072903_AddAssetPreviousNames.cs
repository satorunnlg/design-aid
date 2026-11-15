using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesignAid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetPreviousNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousNames",
                table: "Assets",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousNames",
                table: "Assets");
        }
    }
}
