namespace NlcsLegenda.Core;

public static class TextWrap
{
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
                // Woorden die op zichzelf te lang zijn, hard afbreken.
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
