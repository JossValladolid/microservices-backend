using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNameAndHomeFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable primero para poder rellenar los usuarios existentes antes de exigir NOT NULL + único.
            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Users" u SET "UserName" = t.un
                FROM (
                    SELECT "Id",
                           CASE WHEN row_number() OVER (PARTITION BY base ORDER BY "CreatedAt", "Id") = 1
                                THEN base ELSE base || '-' || substr("Id"::text, 1, 6) END AS un
                    FROM (
                        SELECT "Id", "CreatedAt",
                               CASE WHEN length(b) < 3 THEN b || 'user' ELSE b END AS base
                        FROM (
                            SELECT "Id", "CreatedAt",
                                   left(regexp_replace(lower(split_part("Email", '@', 1)), '[^a-z0-9_-]', '', 'g'), 24) AS b
                            FROM "Users"
                        ) x
                    ) y
                ) t
                WHERE u."Id" = t."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Folders_OwnerId_Home",
                table: "Folders",
                column: "OwnerId",
                unique: true,
                filter: "\"ParentId\" IS NULL");

            // Carpeta home para los usuarios que ya estaban activos.
            migrationBuilder.Sql("""
                INSERT INTO "Folders" ("Id", "OwnerId", "ParentId", "Name", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "Id", NULL, "UserName", now(), now()
                FROM "Users" WHERE "IsActive" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Folders_OwnerId_Home",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "Users");
        }
    }
}
