using System.Text.Json;
using DotnetGitTool.Commands;
using DotnetGitTool.Output;
using DotnetGitTool.State;

namespace DotnetGitTool.Tests;

public sealed class CliOutputTests
{
    private static readonly InstallationRecord Example = new(
        "owner/repository",
        "https://github.com/owner/repository.git",
        null,
        "src/tool.csproj",
        "git.owner.repository",
        "0.0.0-git.0123456789ab",
        "0123456789abcdef",
        "example",
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void HumanListIsColumnarAndNotJson()
    {
        using var stdout = new StringWriter();
        var output = new CliOutput(stdout, new StringWriter());

        output.List(new ListSettings(), [Example]);

        Assert.StartsWith("SOURCE", stdout.ToString());
        Assert.Contains("owner/repository", stdout.ToString());
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(stdout.ToString()));
    }

    [Fact]
    public void JsonListUsesVersionedEnvelope()
    {
        using var stdout = new StringWriter();
        var output = new CliOutput(stdout, new StringWriter());

        output.List(new ListSettings { Json = true }, [Example]);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("meta").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("owner/repository",
            document.RootElement.GetProperty("data").GetProperty("installations")[0].GetProperty("sourceId").GetString());
    }
}
