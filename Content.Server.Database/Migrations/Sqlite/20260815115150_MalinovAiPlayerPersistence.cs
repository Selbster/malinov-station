using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MalinovAiPlayerPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_player_personality",
                columns: table => new
                {
                    persistent_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    sociability = table.Column<float>(type: "REAL", nullable: false),
                    courage = table.Column<float>(type: "REAL", nullable: false),
                    curiosity = table.Column<float>(type: "REAL", nullable: false),
                    laziness = table.Column<float>(type: "REAL", nullable: false),
                    greed = table.Column<float>(type: "REAL", nullable: false),
                    aggression = table.Column<float>(type: "REAL", nullable: false),
                    loyalty = table.Column<float>(type: "REAL", nullable: false),
                    risk_tolerance = table.Column<float>(type: "REAL", nullable: false),
                    authority_respect = table.Column<float>(type: "REAL", nullable: false),
                    professionalism = table.Column<float>(type: "REAL", nullable: false),
                    empathy = table.Column<float>(type: "REAL", nullable: false),
                    honesty = table.Column<float>(type: "REAL", nullable: false),
                    impulsiveness = table.Column<float>(type: "REAL", nullable: false),
                    last_saved_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_player_personality", x => x.persistent_id);
                });

            migrationBuilder.CreateTable(
                name: "ai_player_memory_record",
                columns: table => new
                {
                    ai_player_memory_record_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    persistent_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    importance = table.Column<float>(type: "REAL", nullable: false),
                    emotional_weight = table.Column<float>(type: "REAL", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_player_memory_record", x => x.ai_player_memory_record_id);
                    table.ForeignKey(
                        name: "FK_ai_player_memory_record_ai_player_personality_personality_temp_id",
                        column: x => x.persistent_id,
                        principalTable: "ai_player_personality",
                        principalColumn: "persistent_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_player_memory_record_persistent_id",
                table: "ai_player_memory_record",
                column: "persistent_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_player_memory_record");

            migrationBuilder.DropTable(
                name: "ai_player_personality");
        }
    }
}
