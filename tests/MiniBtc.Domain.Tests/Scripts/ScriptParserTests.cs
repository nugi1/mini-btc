using MiniBtc.Domain.Scripts;

namespace MiniBtc.Domain.Tests.Scripts;

public class ScriptParserTests
{
    [Theory]
    [InlineData("0201ff", new[] { "01ff" })]                 // direct push of 2 bytes
    [InlineData("00", new[] { "" })]                         // OP_0: an empty push
    [InlineData("4c0201ff", new[] { "01ff" })]               // OP_PUSHDATA1
    [InlineData("4d020001ff", new[] { "01ff" })]             // OP_PUSHDATA2
    [InlineData("4e0200000001ff", new[] { "01ff" })]         // OP_PUSHDATA4
    [InlineData("01aa01bb", new[] { "aa", "bb" })]
    public void Reads_every_push_form(string script, string[] expected)
    {
        Assert.True(ScriptParser.TryParsePushes(Convert.FromHexString(script), out var pushes));
        Assert.Equal(expected, pushes.Select(push => Convert.ToHexStringLower(push)).ToArray());
    }

    [Theory]
    [InlineData("0201")]   // push runs past the end
    [InlineData("4c")]     // OP_PUSHDATA1 without its length
    [InlineData("01aa76")] // OP_DUP is not a push
    public void Rejects_anything_else(string script) =>
        Assert.False(ScriptParser.TryParsePushes(Convert.FromHexString(script), out _));
}
