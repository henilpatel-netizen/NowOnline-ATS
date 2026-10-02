using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersTenantRoleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Role_IsActive",
                table: "Users",
                columns: new[] { "TenantId", "Role", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Role_IsActive",
                table: "Users");
        }
    }
}
