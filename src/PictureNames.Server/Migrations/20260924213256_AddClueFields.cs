using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureNames.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddClueFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClueNumber",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClueTeamId",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClueWord",
                table: "Rooms",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClueNumber",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "ClueTeamId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "ClueWord",
                table: "Rooms");
        }
    }
}
