using ProtonMailMcp.Tools;

namespace ProtonMailMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ToolRegistrationTests
{
    [Fact]
    public void ApprovedToolsHaveObjectSchemas()
    {
        foreach (var tool in MailboxTool.CreateApproved())
        {
            Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
            Assert.Equal("object", tool.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
        }
    }
}
