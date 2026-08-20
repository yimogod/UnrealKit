using UnrealKit.Core.Download;

namespace UnrealKit.Tests;

public sealed class NaturalSortComparerTests
{
    [Theory]
    [InlineData("v1.0.9", "v1.0.10")]
    [InlineData("v2", "v10")]
    [InlineData("1.9", "1.10")]
    public void Compare_NumericSegmentsSortByValue(string earlier, string later)
    {
        // 字典序会把 "10" 排到 "9" 前，自然排序必须按数值比较。
        Assert.True(NaturalSortComparer.Instance.Compare(earlier, later) < 0);
        Assert.True(NaturalSortComparer.Instance.Compare(later, earlier) > 0);
    }

    [Theory]
    [InlineData("01", "1")]
    [InlineData("v1.01", "v1.1")]
    public void Compare_LeadingZerosAreEquivalent(string a, string b)
    {
        // 前导零不改变数值大小，但直接逐位比较字符串会误判。
        Assert.Equal(0, NaturalSortComparer.Instance.Compare(a, b));
    }

    [Fact]
    public void Compare_TextSegmentsAreCaseInsensitive()
    {
        Assert.Equal(0, NaturalSortComparer.Instance.Compare("Build", "build"));
        Assert.Equal(0, NaturalSortComparer.Instance.Compare("V10", "v10"));
    }

    [Fact]
    public void Compare_PrefixComesBeforeLongerString()
    {
        // 前缀相同，短者在前。
        Assert.True(NaturalSortComparer.Instance.Compare("v1", "v1.0") < 0);
    }

    [Fact]
    public void Compare_SortsVersionDirectoryNamesByLatest()
    {
        var names = new[] { "v1.0.2", "v1.0.10", "v1.0.9" };

        var sorted = names.OrderBy(name => name, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(["v1.0.2", "v1.0.9", "v1.0.10"], sorted);
    }

    [Fact]
    public void Compare_OrdersMixedTextAndNumericSegments()
    {
        // 数字段与文本段交替比较：先比第一个数字段，再比后续段。
        var names = new[] { "2024.1.9", "2024.1.10", "2024.1.2" };

        var sorted = names.OrderBy(name => name, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(["2024.1.2", "2024.1.9", "2024.1.10"], sorted);
    }
}
