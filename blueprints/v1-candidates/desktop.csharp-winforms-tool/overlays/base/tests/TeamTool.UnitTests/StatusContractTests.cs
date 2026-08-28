using TeamTool.Domain;
using Xunit;

namespace TeamTool.UnitTests;

public sealed class StatusContractTests
{
    [Fact]
    public void StatusPreservesTheApplicationMessage()
    {
        var status = new ToolStatus("ready");

        Assert.Equal("ready", status.Message);
    }
}
