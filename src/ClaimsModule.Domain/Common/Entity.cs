namespace ClaimsModule.Domain.Common;

/// <summary>
/// Base type for all entities. Identity is a SQL-Server-ordered sequential GUID assigned
/// by the domain (see <see cref="SequentialGuid"/>), so domain events can reference the
/// id before the entity is persisted. Equality is by type and id.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }

        Id = id;
    }

    /// <summary>For EF Core materialisation only.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; private init; }

    public bool Equals(Entity? other) =>
        other is not null
        && other.GetType() == GetType()
        && Id != Guid.Empty
        && other.Id == Id;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
