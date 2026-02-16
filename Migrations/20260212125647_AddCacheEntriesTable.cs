using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace isp_report_api.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheEntriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cache_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    cache_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    cache_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    cached_data = table.Column<string>(type: "LONGTEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    filter_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cache_entries", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_cache_entries_cache_key",
                table: "cache_entries",
                column: "cache_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cache_entries_cache_type",
                table: "cache_entries",
                column: "cache_type");

            migrationBuilder.CreateIndex(
                name: "IX_cache_entries_expires_at",
                table: "cache_entries",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cache_entries");
        }
    }
}
