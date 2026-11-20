using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations
{
    /// <inheritdoc />
    public partial class OneRefreshTokenPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Antes se permitían varias sesiones por usuario: se conserva solo la más reciente de cada uno.
            migrationBuilder.Sql("""
                DELETE FROM "Sessions" s
                USING "Sessions" newer
                WHERE s."UserId" = newer."UserId"
                  AND (s."ExpiryDate" < newer."ExpiryDate"
                       OR (s."ExpiryDate" = newer."ExpiryDate" AND s."Id" < newer."Id"));
                """);

            migrationBuilder.DropIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId",
                table: "Sessions",
                column: "UserId");
        }
    }
}
