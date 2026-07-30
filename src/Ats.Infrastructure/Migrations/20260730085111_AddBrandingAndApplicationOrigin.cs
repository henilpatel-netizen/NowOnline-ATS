using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ats.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingAndApplicationOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandAccentColor",
                table: "TenantSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandSidebarTheme",
                table: "TenantSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CareerHeroHeadline",
                table: "TenantSettings",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CareerHeroHeadlineOutlined",
                table: "TenantSettings",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CareerHeroIntro",
                table: "TenantSettings",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedLastPulledAt",
                table: "TenantSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "Applications",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandAccentColor",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "BrandSidebarTheme",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CareerHeroHeadline",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CareerHeroHeadlineOutlined",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CareerHeroIntro",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "FeedLastPulledAt",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Applications");
        }
    }
}
