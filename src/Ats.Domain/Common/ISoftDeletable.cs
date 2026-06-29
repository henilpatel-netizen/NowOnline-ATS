namespace Ats.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}
