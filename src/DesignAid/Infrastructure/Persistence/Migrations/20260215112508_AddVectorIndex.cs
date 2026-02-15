using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesignAid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVectorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Projects_ProjectId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Assets_Name",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_ProjectId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_ProjectId_Name",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Assets");

            migrationBuilder.AddColumn<string>(
                name: "DesignComponentId",
                table: "HandoverHistory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetSubAssets",
                columns: table => new
                {
                    ParentAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    ChildAssetId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetSubAssets", x => new { x.ParentAssetId, x.ChildAssetId });
                    table.ForeignKey(
                        name: "FK_AssetSubAssets_Assets_ChildAssetId",
                        column: x => x.ChildAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetSubAssets_Assets_ParentAssetId",
                        column: x => x.ParentAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VectorIndex",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PartId = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    AssetId = table.Column<string>(type: "TEXT", nullable: true),
                    AssetName = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VectorIndex", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HandoverHistory_DesignComponentId",
                table: "HandoverHistory",
                column: "DesignComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Name",
                table: "Assets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetSubAssets_ChildAssetId",
                table: "AssetSubAssets",
                column: "ChildAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetSubAssets_ParentAssetId",
                table: "AssetSubAssets",
                column: "ParentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VectorIndex_PartId",
                table: "VectorIndex",
                column: "PartId");

            migrationBuilder.AddForeignKey(
                name: "FK_HandoverHistory_Parts_DesignComponentId",
                table: "HandoverHistory",
                column: "DesignComponentId",
                principalTable: "Parts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HandoverHistory_Parts_DesignComponentId",
                table: "HandoverHistory");

            migrationBuilder.DropTable(
                name: "AssetSubAssets");

            migrationBuilder.DropTable(
                name: "VectorIndex");

            migrationBuilder.DropIndex(
                name: "IX_HandoverHistory_DesignComponentId",
                table: "HandoverHistory");

            migrationBuilder.DropIndex(
                name: "IX_Assets_Name",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DesignComponentId",
                table: "HandoverHistory");

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "Assets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Name",
                table: "Assets",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId",
                table: "Assets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_Name",
                table: "Assets",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Projects_ProjectId",
                table: "Assets",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
