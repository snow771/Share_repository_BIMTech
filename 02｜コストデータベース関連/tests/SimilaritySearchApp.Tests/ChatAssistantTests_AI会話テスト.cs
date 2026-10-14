using System.Text.Json.Nodes;

namespace SimilaritySearchApp.Tests;

public class ChatAssistantTests
{
    [Theory]
    [InlineData("filter", AssistantRoute.Filter)]
    [InlineData("Summary", AssistantRoute.Summary)]
    [InlineData("review", AssistantRoute.Review)]
    [InlineData("project", AssistantRoute.Project)]
    [InlineData("chat", AssistantRoute.Chat)]
    [InlineData("unknown", AssistantRoute.Chat)]
    public void Parse_対応を読み取る(string route, AssistantRoute expected)
    {
        var d = ChatAssistantService.Parse(new JsonObject { ["route"] = route, ["reply"] = " こんにちは " });
        Assert.Equal(expected, d.Route);
        Assert.Equal("こんにちは", d.Reply);
    }

    [Fact]
    public void Remember_古い会話を捨て長い発言は切る()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 20; i++) ChatAssistantService.Remember(history, "user", $"発言{i}");
        Assert.Equal(ChatAssistantService.MaxHistory, history.Count);
        Assert.Equal("発言19", history[^1].Content);

        ChatAssistantService.Remember(history, "assistant", new string('あ', 1000));
        Assert.Equal(ChatAssistantService.MaxHistoryChars + 1, history[^1].Content.Length);
    }

    [Fact]
    public void BuildPrompt_状況と会話の流れを渡す()
    {
        var history = new List<ChatMessage> { new("user", "こんにちは"), new("assistant", "こんにちは！") };
        var context = ChatAssistantService.BuildContext(false, StructureType.RC, 0, null, new FilterCriteria(), null);
        var messages = ChatAssistantService.BuildPrompt("使い方を教えて", history, context);

        Assert.Equal("system", messages[0].Role);
        Assert.Contains("データはまだ読み込まれていません", messages[0].Content);
        Assert.Equal(new[] { "こんにちは", "こんにちは！", "使い方を教えて" }, messages.Skip(1).Select(m => m.Content));
    }
}
