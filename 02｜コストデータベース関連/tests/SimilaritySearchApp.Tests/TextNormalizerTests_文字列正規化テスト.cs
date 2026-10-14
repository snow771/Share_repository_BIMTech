namespace SimilaritySearchApp.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void NormKey_matches_python() =>
        Assert.Equal("置床ﾌﾛｰﾘﾝｸﾞ(a)", TextNormalizer.NormKey(" 置床･ﾌﾛｰﾘﾝｸﾞ（A）　"));

    [Fact]
    public void Clean_treats_nan_as_empty() =>
        Assert.Equal("", TextNormalizer.Clean("nan"));

    [Fact]
    public void Clean_replaces_fullwidth_space_and_trims() =>
        Assert.Equal("東京都 港区", TextNormalizer.Clean("　東京都　港区 "));
}
