using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SphotoApp.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedGoogleDriveApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedGoogleDriveApiKey",
                table: "EppConfigs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedGoogleDriveApiKey",
                table: "EppConfigs");
        }
    }
}
