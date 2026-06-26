# EF Core Migration Rules

- Migrations live in `src/Ats.Infrastructure/Migrations`, context `AtsDbContext`.
- Create a migration (allowed):
  `dotnet ef migrations add <Name> --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`
- Applying migrations is a MANUAL developer action. The AI must NOT run `database update`.
  Provide the command for the developer:
  `dotnet ef database update --project src/Ats.Infrastructure --startup-project src/Ats.Web --context AtsDbContext`
- Name migrations in PascalCase describing the change (e.g. `AddJobAndApplication`).
