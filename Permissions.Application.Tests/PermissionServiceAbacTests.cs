using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Policies;
using Permissions.Application.Policies.BuiltIn;
using Permissions.Application.Services;
using Permissions.Domain.Attributes;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Tests;

public sealed class PermissionServiceAbacTests
{
  private readonly Mock<IRelationTupleRepository> _tuples = new();

  public PermissionServiceAbacTests()
  {
    _tuples.Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync((TupleKey key, CancellationToken _) =>
            key.SubjectType == "user" && key.SubjectId == "7" &&
            key.ObjectId == "42");
    _tuples.Setup(r => r.GetByObjectAndRelationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);
  }

  private PermissionService Service(IEnumerable<IAbacPolicy>? policies = null)
  {
    policies ??= [
        new RegionMatchPolicy("report", "viewer"),
        new RegionMatchPolicy("report", "editor"),
        new ResourceStatusPolicy("report", "editor", "locked", "archived"),
        new SensitivityLevelPolicy("report", "viewer"),
        new SensitivityLevelPolicy("report", "editor")
    ];
    return new PermissionService(_tuples.Object,
        new PermissionCheckEngine(_tuples.Object, new Mock<IRoleRepository>().Object),
        new AbacEvaluator(policies));
  }

  private static CheckPermissionRequest Request(
      string subjectId = "7", string relation = "viewer", string objectType = "report") => new(
          "user", subjectId, relation, objectType, "42",
          new SubjectAttributes {
            SubjectType = "user", SubjectId = subjectId, Region = "eu-west", ClearanceLevel = 3
          },
          new ResourceAttributes {
            ResourceType = objectType, ResourceId = "42", Region = "eu-west",
            SensitivityLevel = 2, Status = "draft"
          });

  [Fact]
  public async Task RebacOnlyAccessWorksWithoutAttributesAndMissingGrantDenies()
  {
    var service = Service();
    var granted = new CheckPermissionRequest("user", "7", "viewer", "document", "42");
    Assert.True((await service.CheckAsync(granted)).Allowed);
    Assert.False((await service.CheckAsync(granted with { SubjectId = "8" })).Allowed);
    Assert.False((await service.CheckAsync(Request(subjectId: "8"))).Allowed);
  }

  [Fact]
  public async Task RequiredReportAccessPassesWithCompleteMatchingAttributes()
  {
    Assert.True((await Service().CheckAsync(Request())).Allowed);
    Assert.True((await Service().CheckAsync(Request(relation: "editor"))).Allowed);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task MissingRequiredAttributeObjectIsRejected(bool omitSubject)
  {
    var request = omitSubject
        ? Request() with { SubjectAttributes = null }
        : Request() with { ResourceAttributes = null };
    await Assert.ThrowsAsync<ArgumentException>(() => Service().CheckAsync(request));
  }

  [Theory]
  [InlineData("subject-region")]
  [InlineData("resource-region")]
  [InlineData("clearance")]
  [InlineData("sensitivity")]
  [InlineData("status")]
  public async Task MissingRequiredPropertyIsRejected(string missing)
  {
    var request = Request(relation: "editor");
    var subject = new SubjectAttributes {
      SubjectType = "user", SubjectId = "7",
      Region = missing == "subject-region" ? null : "eu-west",
      ClearanceLevel = missing == "clearance" ? null : 3
    };
    var resource = new ResourceAttributes {
      ResourceType = "report", ResourceId = "42",
      Region = missing == "resource-region" ? null : "eu-west",
      SensitivityLevel = missing == "sensitivity" ? null : 2,
      Status = missing == "status" ? null : "draft"
    };
    await Assert.ThrowsAsync<ArgumentException>(() =>
        Service().CheckAsync(request with {
          SubjectAttributes = subject, ResourceAttributes = resource
        }));
  }

  [Theory]
  [InlineData("subject")]
  [InlineData("resource")]
  [InlineData("resource-type")]
  public async Task MismatchedNestedIdentityIsRejected(string mismatch)
  {
    var request = Request();
    var subject = new SubjectAttributes {
      SubjectType = "user", SubjectId = mismatch == "subject" ? "8" : "7",
      Region = "eu-west", ClearanceLevel = 3
    };
    var resource = new ResourceAttributes {
      ResourceType = mismatch == "resource-type" ? "document" : "report",
      ResourceId = mismatch == "resource" ? "43" : "42",
      Region = "eu-west", SensitivityLevel = 2, Status = "draft"
    };
    await Assert.ThrowsAsync<ArgumentException>(() =>
        Service().CheckAsync(request with {
          SubjectAttributes = subject, ResourceAttributes = resource
        }));
  }

  [Theory]
  [InlineData("clearance", "SensitivityLevelPolicy")]
  [InlineData("region", "RegionMatchPolicy")]
  [InlineData("locked", "ResourceStatusPolicy")]
  [InlineData("archived", "ResourceStatusPolicy")]
  public async Task RequiredPoliciesDenyUnsafeValues(string scenario, string policy)
  {
    var request = Request(relation: "editor");
    var subject = new SubjectAttributes {
      SubjectType = "user", SubjectId = "7",
      Region = scenario == "region" ? "us-east" : "eu-west",
      ClearanceLevel = scenario == "clearance" ? 1 : 3
    };
    var resource = new ResourceAttributes {
      ResourceType = "report", ResourceId = "42", Region = "eu-west",
      SensitivityLevel = 2,
      Status = scenario is "locked" or "archived" ? scenario : "draft"
    };
    var result = await Service().CheckAsync(request with {
      SubjectAttributes = subject, ResourceAttributes = resource
    });
    Assert.False(result.Allowed);
    Assert.Contains(policy, result.Reason);
  }

  [Fact]
  public async Task UnsupportedStatusIsRejectedForRequiredEditorOperation()
  {
    var request = Request(relation: "editor");
    var resource = new ResourceAttributes {
      ResourceType = "report", ResourceId = "42", Region = "eu-west",
      SensitivityLevel = 2, Status = "unrecognized"
    };
    await Assert.ThrowsAsync<ArgumentException>(() =>
        Service().CheckAsync(request with { ResourceAttributes = resource }));
  }

  [Fact]
  public async Task RequiredPolicyNotApplicableNeverAllows()
  {
    var service = Service([new RegionMatchPolicy("report", "viewer")]);
    await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync(Request()));
  }

  [Fact]
  public async Task EvaluatorExceptionNeverAllows()
  {
    var policies = new IAbacPolicy[] {
      new RegionMatchPolicy("report", "viewer"),
      new SensitivityLevelPolicy("report", "viewer"),
      new ThrowingPolicy()
    };
    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        Service(policies).CheckAsync(Request()));
  }

  private sealed class ThrowingPolicy : IAbacPolicy
  {
    public string Name => "ThrowingPolicy";
    public string ResourceType => "report";
    public string Relation => "viewer";
    public bool Evaluate(SubjectAttributes subject, ResourceAttributes resource,
        EnvironmentAttributes environment) => throw new InvalidOperationException("Policy failed");
  }
}
