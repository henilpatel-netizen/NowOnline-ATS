using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FilterUniqueIndexesOnSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Departments_DepartmentId",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Locations_LocationId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_TenantId_Email",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Applications_TenantId_JobId_CandidateId",
                table: "Applications");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_TenantId_Email",
                table: "Candidates",
                columns: new[] { "TenantId", "Email" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TenantId_JobId_CandidateId",
                table: "Applications",
                columns: new[] { "TenantId", "JobId", "CandidateId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Departments_DepartmentId",
                table: "Jobs",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Locations_LocationId",
                table: "Jobs",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Departments_DepartmentId",
                table: "Jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Locations_LocationId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_TenantId_Email",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Applications_TenantId_JobId_CandidateId",
                table: "Applications");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_TenantId_Email",
                table: "Candidates",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TenantId_JobId_CandidateId",
                table: "Applications",
                columns: new[] { "TenantId", "JobId", "CandidateId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Departments_DepartmentId",
                table: "Jobs",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Locations_LocationId",
                table: "Jobs",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
