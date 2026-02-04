using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesignAid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Parts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ArtifactHashesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    MetaDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    StandardIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    PartTypeDiscriminator = table.Column<string>(type: "TEXT", maxLength: 21, nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: true),
                    SurfaceTreatment = table.Column<string>(type: "TEXT", nullable: true),
                    DrawingPath = table.Column<string>(type: "TEXT", nullable: true),
                    LeadTimeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    ManufacturerPartNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SpecificationPath = table.Column<string>(type: "TEXT", nullable: true),
                    PurchasedPart_LeadTimeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: true),
                    StandardName = table.Column<string>(type: "TEXT", nullable: true),
                    StandardNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialGrade = table.Column<string>(type: "TEXT", nullable: true),
                    Finish = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Standards",
                columns: table => new
                {
                    StandardId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ValidationRuleJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Standards", x => x.StandardId);
                });

            migrationBuilder.CreateTable(
                name: "HandoverHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PartId = table.Column<string>(type: "TEXT", nullable: false),
                    CommittedHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OrderDate = table.Column<string>(type: "TEXT", nullable: false),
                    DeliveryDate = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HandoverHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HandoverHistory_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetComponents",
                columns: table => new
                {
                    AssetId = table.Column<string>(type: "TEXT", nullable: false),
                    PartId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetComponents", x => new { x.AssetId, x.PartId });
                    table.ForeignKey(
                        name: "FK_AssetComponents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetComponents_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetComponents_AssetId",
                table: "AssetComponents",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetComponents_PartId",
                table: "AssetComponents",
                column: "PartId");

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
                name: "IX_HandoverHistory_PartId",
                table: "HandoverHistory",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_HandoverHistory_Status",
                table: "HandoverHistory",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_PartNumber",
                table: "Parts",
                column: "PartNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetComponents");

            migrationBuilder.DropTable(
                name: "HandoverHistory");

            migrationBuilder.DropTable(
                name: "Standards");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Parts");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
