using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MalinovLobbyBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tile_order",
                table: "malinov_player_lobby_layout");

            migrationBuilder.CreateTable(
                name: "malinov_player_lobby_tile",
                columns: table => new
                {
                    layout_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tile_id = table.Column<string>(type: "TEXT", nullable: false),
                    board_column = table.Column<int>(type: "INTEGER", nullable: false),
                    board_row = table.Column<int>(type: "INTEGER", nullable: false),
                    width = table.Column<int>(type: "INTEGER", nullable: false),
                    height = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_malinov_player_lobby_tile", x => new { x.layout_user_id, x.tile_id });
                    table.ForeignKey(
                        name: "FK_malinov_player_lobby_tile_malinov_player_lobby_layout_layout_user_id",
                        column: x => x.layout_user_id,
                        principalTable: "malinov_player_lobby_layout",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "malinov_player_lobby_tile");

            migrationBuilder.AddColumn<string>(
                name: "tile_order",
                table: "malinov_player_lobby_layout",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
