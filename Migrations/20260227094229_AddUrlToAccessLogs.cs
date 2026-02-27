using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace isp_report_api.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlToAccessLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "AccessLogs",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Url",
                table: "AccessLogs");
        }
    }
}
