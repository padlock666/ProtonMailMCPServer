using System.Reflection;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class TestCategorizationTests
{
    [Fact]
    public void EveryTestHasExactlyOneRecognizedCategory()
    {
        var allowed = new[] { "Unit", "Protocol", "Mcp", "LiveBridge" };
        foreach (var type in typeof(TestCategorizationTests).Assembly.GetTypes())
        foreach (var method in type.GetMethods())
        {
            if (!method.GetCustomAttributes().Any(attribute => attribute is FactAttribute)) continue;
            var categories = type.GetCustomAttributesData()
                .Concat(method.GetCustomAttributesData())
                .Where(attribute => attribute.AttributeType == typeof(TraitAttribute) &&
                    (string?)attribute.ConstructorArguments[0].Value == "Category")
                .Select(attribute => (string?)attribute.ConstructorArguments[1].Value).ToArray();
            Assert.True(categories.Length == 1 && allowed.Contains(categories[0]),
                $"Missing or ambiguous Category on {type.Name}.{method.Name}");
        }
    }
}
