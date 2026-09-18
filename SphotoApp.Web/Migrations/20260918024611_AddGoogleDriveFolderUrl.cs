using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SphotoApp.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveFolderUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveFolderUrl",
                table: "EppConfigs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleDriveFolderUrl",
                table: "EppConfigs");
        }
    }
}
