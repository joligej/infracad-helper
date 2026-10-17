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
            foreach (var word in paragraph.Split(' '))
            {
                var piece = word;
                while (piece.Length > maxChars)
                {
                    if (line.Length > 0)
                    {
                        result.Add(line.ToString());
                        line.Clear();
                    }
                    result.Add(piece[..maxChars]);
                    piece = piece[maxChars..];
                }

                int extra = line.Length == 0 ? piece.Length : line.Length + 1 + piece.Length;
                if (extra > maxChars && line.Length > 0)
                {
                    result.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');
                line.Append(piece);
            }
            result.Add(line.ToString());
        }
        return result;
    }
}
