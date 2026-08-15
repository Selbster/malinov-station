using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
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
                    persistent_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sociability = table.Column<float>(type: "real", nullable: false),
                    courage = table.Column<float>(type: "real", nullable: false),
                    curiosity = table.Column<float>(type: "real", nullable: false),
                    laziness = table.Column<float>(type: "real", nullable: false),
                    greed = table.Column<float>(type: "real", nullable: false),
                    aggression = table.Column<float>(type: "real", nullable: false),
                    loyalty = table.Column<float>(type: "real", nullable: false),
                    risk_tolerance = table.Column<float>(type: "real", nullable: false),
                    authority_respect = table.Column<float>(type: "real", nullable: false),
                    professionalism = table.Column<float>(type: "real", nullable: false),
                    empathy = table.Column<float>(type: "real", nullable: false),
                    honesty = table.Column<float>(type: "real", nullable: false),
                    impulsiveness = table.Column<float>(type: "real", nullable: false),
                    last_saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_player_personality", x => x.persistent_id);
                });

            migrationBuilder.CreateTable(
                name: "ai_player_memory_record",
                columns: table => new
                {
                    ai_player_memory_record_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    persistent_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    importance = table.Column<float>(type: "real", nullable: false),
                    emotional_weight = table.Column<float>(type: "real", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_player_memory_record", x => x.ai_player_memory_record_id);
                    table.ForeignKey(
                        name: "FK_ai_player_memory_record_ai_player_personality_personality_t~",
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
