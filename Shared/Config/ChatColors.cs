using System.Text;
using System.Text.RegularExpressions;

namespace Finestats.Config;

// SwiftlyS2 performs the actual color conversion in SendChat. Keep tags intact
// while budgeting their one-byte wire representation, including a final reset.
public static class ChatColors
{
    private static readonly Regex Tags = new(@"\[(?:white|darkred|green|lightyellow|lightblue|olive|lime|red|lightpurple|purple|grey|yellow|gold|silver|blue|darkblue|bluegrey|magenta|lightred|orange|default|/)\]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Plain(string text) => Tags.Replace(text, "");

    public static string Limit(string text, int maxBytes)
    {
        var result = new StringBuilder();
        int bytes = 0, position = 0;
        bool colored = Tags.IsMatch(text);
        int budget = maxBytes - (colored ? 1 : 0);
        foreach (Match tag in Tags.Matches(text))
        {
            if (!Append(text[position..tag.Index])) return Finish();
            if (bytes == budget) return Finish();
            result.Append(tag.Value); bytes++;
            position = tag.Index + tag.Length;
        }
        Append(text[position..]);
        return Finish();

        bool Append(string part)
        {
            foreach (var rune in part.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > budget) return false;
                result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
            }
            return true;
        }
        string Finish() => colored ? result.Append("[/]").ToString() : result.ToString();
    }
}
