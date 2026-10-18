namespace NlcsLegenda.Core;

public static class TextWrap
{
    public static string BreakLongWords(string text, Func<string, bool> fits)
    {
        var output = new System.Text.StringBuilder();
        int start = 0;
        while (start < text.Length)
        {
            if (char.IsWhiteSpace(text[start])) { output.Append(text[start++]); continue; }
            int end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            var word = text[start..end];
            while (!fits(word))
            {
                var positions = System.Globalization.StringInfo.ParseCombiningCharacters(word);
                if (positions.Length < 2) break;
                int lo = 1, hi = positions.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (fits(word[..positions[mid]])) lo = mid; else hi = mid - 1;
                }
                int split = positions[lo];
                output.Append(word[..split]).Append('\n');
                word = word[split..];
            }
            output.Append(word);
            start = end;
        }
        return output.ToString();
    }

    public static List<string> Wrap(string? text, int maxChars)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
            return result;
        if (maxChars < 1)
            maxChars = 1;

        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length == 0)
            {
                result.Add(string.Empty);
                continue;
            }

            var line = new System.Text.StringBuilder();
            int lineLength = 0;
            foreach (var piece in paragraph.Split(' ').SelectMany(word => SplitWord(word, maxChars)))
            {
                int length = new System.Globalization.StringInfo(piece).LengthInTextElements;
                int extra = lineLength == 0 ? length : lineLength + 1 + length;
                if (extra > maxChars && lineLength > 0)
                {
                    result.Add(line.ToString());
                    line.Clear();
                    lineLength = 0;
                }
                if (lineLength > 0) { line.Append(' '); lineLength++; }
                line.Append(piece);
                lineLength += length;
            }
            result.Add(line.ToString());
        }
        return result;
    }

    private static IEnumerable<string> SplitWord(string word, int maxChars)
    {
        var positions = System.Globalization.StringInfo.ParseCombiningCharacters(word);
        if (positions.Length == 0) { yield return string.Empty; yield break; }
        for (int start = 0; start < positions.Length; start += maxChars)
        {
            int end = Math.Min(positions.Length, start + maxChars);
            yield return word[positions[start]..(end == positions.Length ? word.Length : positions[end])];
        }
    }
}
