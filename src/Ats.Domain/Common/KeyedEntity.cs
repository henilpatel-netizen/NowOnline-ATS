namespace Ats.Domain.Common;

public abstract class KeyedEntity
{
    public int Id { get; set; }
    public Guid Key { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
