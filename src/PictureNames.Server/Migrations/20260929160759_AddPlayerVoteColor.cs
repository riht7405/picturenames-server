using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureNames.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerVoteColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoteColor",
                table: "Players",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoteColor",
                table: "Players");
        }
    }
}
