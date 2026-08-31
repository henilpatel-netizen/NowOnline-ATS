using System.Linq.Expressions;
using Ats.Application.Departments;
using Ats.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ats.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository : NamedLookupRepository<Department>, IDepartmentRepository
{
    public DepartmentRepository(AtsDbContext db) : base(db) { }

    protected override DbSet<Department> Set => Db.Departments;
    protected override Expression<Func<Department, string>> NameSelector => d => d.Name;
    protected override Expression<Func<Job, bool>> ReferencedByJob(int id) => j => j.DepartmentId == id;
}
