using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAndWebhookDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralToolApiKey",
                table: "TenantSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ExternalVacancyId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ExternalCandidateId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    CandidateStatus = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutboxMessageId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_OutboxMessages_OutboxMessageId",
                        column: x => x.OutboxMessageId,
                        principalTable: "OutboxMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_ApplicationId_Id",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "ApplicationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_TenantId_Status_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_OutboxMessageId",
                table: "WebhookDeliveries",
                column: "OutboxMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_TenantId_OutboxMessageId",
                table: "WebhookDeliveries",
                columns: new[] { "TenantId", "OutboxMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ReferralToolApiKey",
                table: "TenantSettings");
        }
    }
}
