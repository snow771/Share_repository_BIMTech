namespace SimilaritySearchApp.Tests;

public class LlmResponseParserTests
{
    [Fact]
    public void Parse_code_fence_and_preamble()
    {
        Assert.Equal("a", LlmResponseParser.ParseJsonObject("結果:\n```json\n{\"k\":\"a\"}\n```")["k"]!.GetValue<string>());
        Assert.Equal(1, LlmResponseParser.ParseJsonObject("前置き {\"n\":1} 後置き")["n"]!.GetValue<int>());
    }

    [Fact]
    public void Parse_failure_throws() =>
        Assert.Throws<InvalidOperationException>(() => LlmResponseParser.ParseJsonObject("JSONなし"));

    [Fact]
    public void Ndjson_is_concatenated()
    {
        var o = LlmResponseParser.ParseBody("{\"response\":\"こん\"}\n{\"response\":\"にちは\",\"done\":true}");
        Assert.Equal("こんにちは", LlmResponseParser.ExtractText(o));
    }

    [Fact]
    public void ExtractText_prefers_message_content()
    {
        var o = LlmResponseParser.ParseBody("{\"message\":{\"role\":\"assistant\",\"content\":\" 本文 \"},\"response\":\"x\"}");
        Assert.Equal("本文", LlmResponseParser.ExtractText(o));
    }

    [Fact]
    public void Strip_think() =>
        Assert.Equal("答え", LlmResponseParser.StripThink("<think>x\ny</think> 答え"));

    [Fact]
    public void NoThink_prefixes_system()
    {
        var m = LlmResponseParser.WithNoThink(new[] { new ChatMessage("system", "S"), new ChatMessage("user", "U") });
        Assert.StartsWith("/no_think", m[0].Content);
        Assert.EndsWith("S", m[0].Content);
    }

    [Fact]
    public void NoThink_inserts_system_when_missing()
    {
        var m = LlmResponseParser.WithNoThink(new[] { new ChatMessage("user", "U") });
        Assert.Equal("system", m[0].Role);
        Assert.Equal(2, m.Count);
    }
}
