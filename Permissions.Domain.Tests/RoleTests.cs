using Permissions.Domain.Entities;

namespace Permissions.Domain.Tests;

public sealed class RoleTests
{
  [Fact]
  public void Create_ValidName_ReturnsRole()
  {
    var role = Role.Create("editor");

    Assert.Equal("editor", role.Name);
    Assert.Equal("EDITOR", role.NormalizedName);
    Assert.NotEqual(Guid.Empty, role.Id);
  }

  [Fact]
  public void Create_EmptyName_ThrowsArgumentException()
  {
    Assert.Throws<ArgumentException>(() => Role.Create(""));
  }

  [Fact]
  public void SetParent_ValidParent_SetsParentRoleId()
  {
    var parent = Role.Create("viewer");
    var child = Role.Create("editor");

    child.SetParent(parent);

    Assert.Equal(parent.Id, child.ParentRoleId);
  }

  [Fact]
  public void SetParent_Self_ThrowsInvalidOperationException()
  {
    var role = Role.Create("editor");

    Assert.Throws<InvalidOperationException>(() => role.SetParent(role));
  }

  [Fact]
  public void NormalizedName_IsAlwaysUpperInvariant()
  {
    var role = Role.Create("Admin");

    Assert.Equal("ADMIN", role.NormalizedName);
  }
}