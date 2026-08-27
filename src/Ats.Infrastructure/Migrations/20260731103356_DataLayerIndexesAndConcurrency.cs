using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DataLayerIndexesAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TenantSettings",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PipelineTemplates",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextAttemptAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_TenantId_LastName_FirstName",
                table: "Candidates",
                columns: new[] { "TenantId", "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TenantId_CandidateId",
                table: "Applications",
                columns: new[] { "TenantId", "CandidateId" });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TenantId_Status",
                table: "Applications",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_TenantId_ToStageId_OccurredAt",
                table: "ApplicationEvents",
                columns: new[] { "TenantId", "ToStageId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_NextAttemptAt",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_TenantId_LastName_FirstName",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Applications_TenantId_CandidateId",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Applications_TenantId_Status",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationEvents_TenantId_ToStageId_OccurredAt",
                table: "ApplicationEvents");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PipelineTemplates");
        }
    }
}
