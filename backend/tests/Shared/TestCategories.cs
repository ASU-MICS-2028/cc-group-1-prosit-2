using Xunit.Abstractions;
using Xunit.Sdk;

namespace HealthConnect.Tests.Shared;

/// <summary>Marks a fast test with no I/O. Run only these with: dotnet test --filter Category=Unit</summary>
[TraitDiscoverer("HealthConnect.Tests.Shared.UnitCategoryDiscoverer", "HealthConnect.Tests.Shared")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class UnitTestAttribute : Attribute, ITraitAttribute;

/// <summary>Marks a test that needs the API in memory and/or a real database (Docker).</summary>
[TraitDiscoverer("HealthConnect.Tests.Shared.IntegrationCategoryDiscoverer", "HealthConnect.Tests.Shared")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IntegrationTestAttribute : Attribute, ITraitAttribute;

public sealed class UnitCategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        => [new("Category", "Unit")];
}

public sealed class IntegrationCategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
        => [new("Category", "Integration")];
}
