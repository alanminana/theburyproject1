using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProject.Migrations
{
    /// <inheritdoc />
    public partial class AddMercadoLibreCategoryCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MercadoLibreCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ParentCategoryId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PathFromRootJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChildrenJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsLeaf = table.Column<bool>(type: "bit", nullable: false),
                    ListingAllowed = table.Column<bool>(type: "bit", nullable: false),
                    BuyingAllowed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AttributeTypes = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CatalogDomain = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Vertical = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SubVertical = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    MaxTitleLength = table.Column<int>(type: "int", nullable: true),
                    MaxPicturesPerItem = table.Column<int>(type: "int", nullable: true),
                    MaxVariationsAllowed = table.Column<int>(type: "int", nullable: true),
                    ItemConditionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BuyingModesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShippingOptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalItemsInThisCategory = table.Column<int>(type: "int", nullable: true),
                    Permalink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Picture = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreCategorySyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LastContentCreated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastContentMd5 = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    LastImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImportedCategories = table.Column<int>(type: "int", nullable: false),
                    ImportedAttributes = table.Column<int>(type: "int", nullable: false),
                    LeafCategories = table.Column<int>(type: "int", nullable: false),
                    ListingAllowedCategories = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreCategorySyncStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MercadoLibreCategoryAttributes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AttributeId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Hierarchy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Relevance = table.Column<int>(type: "int", nullable: true),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    CatalogRequired = table.Column<bool>(type: "bit", nullable: false),
                    ConditionalRequired = table.Column<bool>(type: "bit", nullable: false),
                    NewRequired = table.Column<bool>(type: "bit", nullable: false),
                    ReadOnly = table.Column<bool>(type: "bit", nullable: false),
                    Hidden = table.Column<bool>(type: "bit", nullable: false),
                    AllowVariations = table.Column<bool>(type: "bit", nullable: false),
                    VariationAttribute = table.Column<bool>(type: "bit", nullable: false),
                    Multivalued = table.Column<bool>(type: "bit", nullable: false),
                    ValueMaxLength = table.Column<int>(type: "int", nullable: true),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowedUnitsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AttributeGroupId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AttributeGroupName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Hint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Tooltip = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CategoryFk = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MercadoLibreCategoryAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MercadoLibreCategoryAttributes_MercadoLibreCategories_CategoryFk",
                        column: x => x.CategoryFk,
                        principalTable: "MercadoLibreCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategories_IsLeaf",
                table: "MercadoLibreCategories",
                column: "IsLeaf");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategories_ListingAllowed",
                table: "MercadoLibreCategories",
                column: "ListingAllowed");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategories_Name",
                table: "MercadoLibreCategories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategories_ParentCategoryId",
                table: "MercadoLibreCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategories_SiteId_CategoryId",
                table: "MercadoLibreCategories",
                columns: new[] { "SiteId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategoryAttributes_CategoryFk",
                table: "MercadoLibreCategoryAttributes",
                column: "CategoryFk");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategoryAttributes_CategoryId",
                table: "MercadoLibreCategoryAttributes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategoryAttributes_ConditionalRequired",
                table: "MercadoLibreCategoryAttributes",
                column: "ConditionalRequired");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategoryAttributes_Required",
                table: "MercadoLibreCategoryAttributes",
                column: "Required");

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategoryAttributes_SiteId_CategoryId_AttributeId",
                table: "MercadoLibreCategoryAttributes",
                columns: new[] { "SiteId", "CategoryId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MercadoLibreCategorySyncStates_SiteId",
                table: "MercadoLibreCategorySyncStates",
                column: "SiteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MercadoLibreCategoryAttributes");

            migrationBuilder.DropTable(
                name: "MercadoLibreCategorySyncStates");

            migrationBuilder.DropTable(
                name: "MercadoLibreCategories");
        }
    }
}
