using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyItems.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Modifiers_Items_ItemId",
                table: "Modifiers");

            migrationBuilder.DropIndex(
                name: "IX_Modifiers_ItemId",
                table: "Modifiers");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "Modifiers",
                type: "MEDIUMINT(9)",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "MEDIUMINT(9)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Modifiers",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Items",
                type: "MEDIUMINT(9)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "MEDIUMINT(9)")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Description",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.CreateIndex(
                name: "IX_Modifiers_ItemId_Slug_Value",
                table: "Modifiers",
                columns: new[] { "ItemId", "Slug", "Value" });

            migrationBuilder.AddForeignKey(
                name: "FK_Modifiers_Items_ItemId",
                table: "Modifiers",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Modifiers_Items_ItemId",
                table: "Modifiers");

            migrationBuilder.DropIndex(
                name: "IX_Modifiers_ItemId_Slug_Value",
                table: "Modifiers");

            migrationBuilder.AlterColumn<int>(
                name: "ItemId",
                table: "Modifiers",
                type: "MEDIUMINT(9)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "MEDIUMINT(9)");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Modifiers",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Items",
                type: "MEDIUMINT(9)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "MEDIUMINT(9)")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Description",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.CreateIndex(
                name: "IX_Modifiers_ItemId",
                table: "Modifiers",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Modifiers_Items_ItemId",
                table: "Modifiers",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id");
        }
    }
}
