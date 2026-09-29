using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVacancySyncOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ApplicationId",
                table: "OutboxMessages",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "JobId",
                table: "OutboxMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "OutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "OutboxMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_JobId_Id",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "JobId", "Id" },
                filter: "[JobId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // VacancySync rows cannot exist in the old schema (they would come back as candidate messages for application 0); they are regenerated with "Sync vacancies now".
            migrationBuilder.Sql("DELETE d FROM WebhookDeliveries d JOIN OutboxMessages m ON m.Id = d.OutboxMessageId WHERE m.Kind = 1;");
            migrationBuilder.Sql("DELETE FROM OutboxMessages WHERE Kind = 1;");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_TenantId_JobId_Id",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "OutboxMessages");

            migrationBuilder.AlterColumn<int>(
                name: "ApplicationId",
                table: "OutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
