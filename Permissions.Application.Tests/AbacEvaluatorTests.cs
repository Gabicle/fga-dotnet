using Permissions.Application.Policies;
using Permissions.Application.Policies.BuiltIn;
using Permissions.Domain.Attributes;

namespace Permissions.Application.Tests;

public sealed class AbacEvaluatorTests
{
  private readonly EnvironmentAttributes _defaultEnvironment = new();

  [Fact]
  public void Evaluate_NoPoliciesForResourceType_ReturnsNotApplicable()
  {
    var evaluator = new AbacEvaluator([]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42" },
        _defaultEnvironment);

    Assert.False(result.IsApplicable);
    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_RegionMatch_SameRegion_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator([new RegionMatchPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", Region = "eu-west" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Region = "eu-west" },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_RegionMatch_DifferentRegion_ReturnsDenied()
  {
    var evaluator = new AbacEvaluator([new RegionMatchPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", Region = "us-east" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Region = "eu-west" },
        _defaultEnvironment);

    Assert.False(result.IsAllowed);
    Assert.Equal("RegionMatchPolicy", result.DeniedByPolicy);
  }

  [Fact]
  public void Evaluate_RegionMatch_NullRegion_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator([new RegionMatchPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", Region = null },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Region = "eu-west" },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_ResourceStatus_NotLocked_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator(
        [new ResourceStatusPolicy("report", "editor", "locked", "archived")]);

    var result = evaluator.Evaluate(
        "editor",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Status = "draft" },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_ResourceStatus_Locked_ReturnsDenied()
  {
    var evaluator = new AbacEvaluator(
        [new ResourceStatusPolicy("report", "editor", "locked", "archived")]);

    var result = evaluator.Evaluate(
        "editor",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Status = "locked" },
        _defaultEnvironment);

    Assert.False(result.IsAllowed);
    Assert.Equal("ResourceStatusPolicy", result.DeniedByPolicy);
  }

  [Fact]
  public void Evaluate_ResourceStatus_Archived_ReturnsDenied()
  {
    var evaluator = new AbacEvaluator(
        [new ResourceStatusPolicy("report", "editor", "locked", "archived")]);

    var result = evaluator.Evaluate(
        "editor",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7" },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", Status = "archived" },
        _defaultEnvironment);

    Assert.False(result.IsAllowed);
    Assert.Equal("ResourceStatusPolicy", result.DeniedByPolicy);
  }

  [Fact]
  public void Evaluate_SensitivityLevel_SufficientClearance_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator(
        [new SensitivityLevelPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", ClearanceLevel = 4 },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", SensitivityLevel = 3 },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_SensitivityLevel_InsufficientClearance_ReturnsDenied()
  {
    var evaluator = new AbacEvaluator(
        [new SensitivityLevelPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", ClearanceLevel = 2 },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", SensitivityLevel = 4 },
        _defaultEnvironment);

    Assert.False(result.IsAllowed);
    Assert.Equal("SensitivityLevelPolicy", result.DeniedByPolicy);
  }

  [Fact]
  public void Evaluate_SensitivityLevel_ExactMatch_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator(
        [new SensitivityLevelPolicy("report", "viewer")]);

    var result = evaluator.Evaluate(
        "viewer",
        new SubjectAttributes { SubjectType = "user", SubjectId = "7", ClearanceLevel = 3 },
        new ResourceAttributes { ResourceType = "report", ResourceId = "42", SensitivityLevel = 3 },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_MultiplePolicies_AllPass_ReturnsAllowed()
  {
    var evaluator = new AbacEvaluator([
        new RegionMatchPolicy("report", "editor"),
            new ResourceStatusPolicy("report", "editor", "locked"),
            new SensitivityLevelPolicy("report", "editor")
    ]);

    var result = evaluator.Evaluate(
        "editor",
        new SubjectAttributes
        {
          SubjectType = "user",
          SubjectId = "7",
          Region = "eu-west",
          ClearanceLevel = 4
        },
        new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          Status = "draft",
          SensitivityLevel = 3
        },
        _defaultEnvironment);

    Assert.True(result.IsAllowed);
  }

  [Fact]
  public void Evaluate_MultiplePolicies_OneFails_ReturnsDenied()
  {
    var evaluator = new AbacEvaluator([
        new RegionMatchPolicy("report", "editor"),
            new ResourceStatusPolicy("report", "editor", "locked"),
            new SensitivityLevelPolicy("report", "editor")
    ]);

    var result = evaluator.Evaluate(
        "editor",
        new SubjectAttributes
        {
          SubjectType = "user",
          SubjectId = "7",
          Region = "eu-west",
          ClearanceLevel = 2       // insufficient clearance
        },
        new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          Status = "draft",
          SensitivityLevel = 4     //requires level 4
        },
        _defaultEnvironment);

    Assert.False(result.IsAllowed);
    Assert.Equal("SensitivityLevelPolicy", result.DeniedByPolicy);
  }
}