using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Modules.Home.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126121116_InitialMigration : Migration
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
            values: new object[] { new Guid("01a217c4-61e0-7693-b43b-c1deaf2cf518"), false, new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0 });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Presences",
            schema: "home");
    }
}
