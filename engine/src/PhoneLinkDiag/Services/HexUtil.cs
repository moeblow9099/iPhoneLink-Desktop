using System.Text;

namespace PhoneLinkDiag.Services;

public static class HexUtil
{
    public static string ToHex(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "<empty>";
        var sb = new StringBuilder(data.Length * 3);
        foreach (var b in data)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
