namespace Permissions.Domain.Entities;

public sealed class Role
{
  public Guid Id { get; private set; }
  public string Name { get; private set; } = null!;
  public string NormalizedName { get; private set; } = null!;
  public string? Description { get; private set; }
  public Guid? ParentRoleId { get; private set; }
  public Role? ParentRole { get; private set; }
  public IReadOnlyCollection<Role> ChildRoles => _childRoles.AsReadOnly();
  public DateTime CreatedAt { get; private set; }

  private readonly List<Role> _childRoles = [];

  private Role() { }

  public static Role Create(string name, string? description = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    return new Role
    {
      Id = Guid.NewGuid(),
      Name = name,
      NormalizedName = name.ToUpperInvariant(),
      Description = description,
      CreatedAt = DateTime.UtcNow
    };
  }

  public void SetParent(Role parent)
  {
    ArgumentNullException.ThrowIfNull(parent);

    if (parent.Id == Id)
      throw new InvalidOperationException("A role cannot be its own parent.");

    ParentRoleId = parent.Id;
    ParentRole = parent;
  }

  public void SetParentId(Guid parentRoleId)
  {
    ParentRoleId = parentRoleId;
  }
  public override string ToString() => Name;
}