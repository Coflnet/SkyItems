using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkyItems.Migrations
{
    public partial class UpdateModifierIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Modifiers_Slug_Value",
                table: "Modifiers");

            migrationBuilder.CreateIndex(
                name: "IX_Modifiers_Slug_Value_FoundCount",
                table: "Modifiers",
                columns: new[] { "Slug", "Value", "FoundCount" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Modifiers_Slug_Value_FoundCount",
                table: "Modifiers");

            migrationBuilder.CreateIndex(
                name: "IX_Modifiers_Slug_Value",
                table: "Modifiers",
                columns: new[] { "Slug", "Value" });
        }
    }
}
