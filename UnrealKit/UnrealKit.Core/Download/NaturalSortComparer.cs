namespace UnrealKit.Core.Download;

/// <summary>
/// 目录名的自然排序比较器：把数字段当作数值、其余段当作文本逐段比较，
/// 使 <c>v10</c> 排在 <c>v9</c> 之后，而不是按字典序排到 <c>v1</c> 后、<c>v2</c> 前。
/// 用于从 FTP 父目录下选出「最新」的版本子目录。
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static NaturalSortComparer Instance { get; } = new();

    private NaturalSortComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var indexX = 0;
        var indexY = 0;
        while (indexX < x.Length && indexY < y.Length)
        {
            var charX = x[indexX];
            var charY = y[indexY];

            if (char.IsDigit(charX) && char.IsDigit(charY))
            {
                // 跳过前导零后先比数字段长度，再逐位比数字——避免数值累加溢出，
                // 也正确处理 "2" 与 "10"、"01" 与 "1" 这类关系。
                var startX = indexX;
                while (startX < x.Length && x[startX] == '0')
                {
                    startX++;
                }

                var startY = indexY;
                while (startY < y.Length && y[startY] == '0')
                {
                    startY++;
                }

                var endX = startX;
                while (endX < x.Length && char.IsDigit(x[endX]))
                {
                    endX++;
                }

                var endY = startY;
                while (endY < y.Length && char.IsDigit(y[endY]))
                {
                    endY++;
                }

                var lengthX = endX - startX;
                var lengthY = endY - startY;
                if (lengthX != lengthY)
                {
                    return lengthX.CompareTo(lengthY);
                }

                var digitComparison = string.Compare(x, startX, y, startY, lengthX, StringComparison.Ordinal);
                if (digitComparison != 0)
                {
                    return digitComparison;
                }

                indexX = endX;
                indexY = endY;
            }
            else
            {
                var comparison = char.ToUpperInvariant(charX).CompareTo(char.ToUpperInvariant(charY));
                if (comparison != 0)
                {
                    return comparison;
                }

                indexX++;
                indexY++;
            }
        }

        // 前缀相同，短者在前。
        return (x.Length - indexX).CompareTo(y.Length - indexY);
    }
}
