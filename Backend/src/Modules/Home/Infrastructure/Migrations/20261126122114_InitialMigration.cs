using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Modules.Home.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126122114_InitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "home");

        migrationBuilder.CreateTable(
            name: "Presences",
            schema: "home",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeskIsBusy = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Presences", x => x.Id);
            });

        migrationBuilder.InsertData(
            schema: "home",
            table: "Presences",
            columns: new[] { "Id", "DeskIsBusy", "LastSeenAt", "Status" },
            values: new object[] { new Guid("01a217cc-f3c5-752e-8438-65a412278e23"), false, new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0 });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Presences",
            schema: "home");
    }
}
