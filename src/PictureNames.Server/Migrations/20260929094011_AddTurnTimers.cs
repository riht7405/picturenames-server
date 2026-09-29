using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PictureNames.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SpymasterDeadlineUtc",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TurnDeadlineUtc",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TurnNumber",
                table: "Rooms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TurnStartedAtUtc",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusPerCorrectSeconds",
                table: "GameSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FirstSpymasterSeconds",
                table: "GameSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OperativeSeconds",
                table: "GameSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SpymasterSeconds",
                table: "GameSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpymasterDeadlineUtc",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "TurnDeadlineUtc",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "TurnNumber",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "TurnStartedAtUtc",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "BonusPerCorrectSeconds",
                table: "GameSettings");

            migrationBuilder.DropColumn(
                name: "FirstSpymasterSeconds",
                table: "GameSettings");

            migrationBuilder.DropColumn(
                name: "OperativeSeconds",
                table: "GameSettings");

            migrationBuilder.DropColumn(
                name: "SpymasterSeconds",
                table: "GameSettings");
        }
    }
}
