using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaFeeder.Data.Migrations
{
    /// <inheritdoc />
    public partial class add_WatchOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WatchOrder",
                table: "YtManagerApp_subscription",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WatchOrder",
                table: "YtManagerApp_subscription");
        }
    }
}
