using DotnetGitTool.Source;
using DotnetGitTool.Infrastructure;

namespace DotnetGitTool.Tests;

public sealed class SourceSpecParserTests
{
    private readonly SourceSpecParser parser = new();

    [Fact]
    public void ParsesGitHubSlugAndEmbeddedRef()
    {
        var source = parser.Parse("JKamsker/bookmeta-cli@v1.2.0");

        Assert.Equal("https://github.com/JKamsker/bookmeta-cli.git", source.CloneUrl);
        Assert.Equal("JKamsker/bookmeta-cli", source.SourceId);
        Assert.Equal("v1.2.0", source.RequestedRef);
    }

    [Fact]
    public void ExplicitRefOverridesEmbeddedRef()
    {
        var source = parser.Parse("JKamsker/bookmeta-cli@old", "new");

        Assert.Equal("new", source.RequestedRef);
    }

    [Fact]
    public void NormalizesGitHubUrlToSlugIdentity()
    {
        var source = parser.Parse("https://github.com/JKamsker/bookmeta-cli.git");

        Assert.Equal("JKamsker/bookmeta-cli", source.SourceId);
        Assert.Null(source.RequestedRef);
    }

    [Fact]
    public void RejectsRefThatCouldBeParsedAsAnOption()
    {
        var exception = Assert.Throws<CliException>(() => parser.Parse("JKamsker/bookmeta-cli", "--upload-pack=evil"));

        Assert.Equal("invalid_ref", exception.Kind);
    }
}
