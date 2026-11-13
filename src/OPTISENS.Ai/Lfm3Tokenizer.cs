using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OPTISENS.Ai;

/// <summary>
/// Pure C# HuggingFace Byte-Level BPE tokenizer reading <c>tokenizer.json</c> directly
/// (no <c>genai_config.json</c> or duplicate GenAI model load required).
/// </summary>
public sealed partial class Lfm3Tokenizer : IDisposable
{
    private static readonly char[] ByteToChar = BuildByteToChar();
    private static readonly Dictionary<char, byte> CharToByte = BuildCharToByte(ByteToChar);

    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<int, string> _idToToken;
    private readonly Dictionary<(string, string), int> _merges;
    private readonly Dictionary<string, int> _specialTokens;
    private readonly HashSet<int> _specialIds;
    private readonly Regex? _specialRegex;
    private readonly Dictionary<string, int[]> _bpeCache = new();

    [GeneratedRegex(@"(?i:'s|'t|'re|'ve|'m|'ll|'d)|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}{1,3}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+")]
    private static partial Regex PreTokenizeRegex();

    public Lfm3Tokenizer(string modelDir)
    {
        string path = Path.Combine(modelDir, "tokenizer.json");
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var model = root.GetProperty("model");
        var vocabEl = model.GetProperty("vocab");
        _vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        _idToToken = new Dictionary<int, string>();
        foreach (var prop in vocabEl.EnumerateObject())
        {
            int id = prop.Value.GetInt32();
            _vocab[prop.Name] = id;
            _idToToken[id] = prop.Name;
        }

        _merges = new Dictionary<(string, string), int>();
        if (model.TryGetProperty("merges", out var mergesEl))
        {
            int rank = 0;
            foreach (var entry in mergesEl.EnumerateArray())
            {
                string a, b;
                if (entry.ValueKind == JsonValueKind.String)
                {
                    var parts = entry.GetString()!.Split(' ');
                    if (parts.Length != 2) continue;
                    (a, b) = (parts[0], parts[1]);
                }
                else if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() == 2)
                {
                    a = entry[0].GetString()!;
                    b = entry[1].GetString()!;
                }
                else
                {
                    continue;
                }
                _merges[(a, b)] = rank++;
            }
        }

        _specialTokens = new Dictionary<string, int>(StringComparer.Ordinal);
        _specialIds = new HashSet<int>();
        if (root.TryGetProperty("added_tokens", out var addedEl))
        {
            foreach (var tok in addedEl.EnumerateArray())
            {
                int id = tok.GetProperty("id").GetInt32();
                string content = tok.GetProperty("content").GetString() ?? "";
                bool special = tok.TryGetProperty("special", out var sp) && sp.GetBoolean();
                _vocab[content] = id;
                _idToToken[id] = content;
                if (special || content.StartsWith("<|", StringComparison.Ordinal))
                {
                    _specialTokens[content] = id;
                    _specialIds.Add(id);
                }
            }
        }

        if (_specialTokens.Count > 0)
        {
            var pattern = string.Join("|",
                _specialTokens.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            _specialRegex = new Regex(pattern, RegexOptions.Compiled);
        }
    }

    public int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var ids = new List<int>();

        if (_specialRegex is null)
        {
            EncodePlainSegment(text, ids);
            return [.. ids];
        }

        int pos = 0;
        foreach (Match m in _specialRegex.Matches(text))
        {
            if (m.Index > pos)
                EncodePlainSegment(text[pos..m.Index], ids);
            ids.Add(_specialTokens[m.Value]);
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            EncodePlainSegment(text[pos..], ids);

        return [.. ids];
    }

    private void EncodePlainSegment(string segment, List<int> ids)
    {
        foreach (Match m in PreTokenizeRegex().Matches(segment))
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(m.Value);
            char[] mapped = new char[utf8.Length];
            for (int i = 0; i < utf8.Length; i++)
                mapped[i] = ByteToChar[utf8[i]];
            string word = new(mapped);

            if (_bpeCache.TryGetValue(word, out var cached))
            {
                ids.AddRange(cached);
                continue;
            }

            var encoded = ApplyBpe(word);
            if (_bpeCache.Count < 50_000)
                _bpeCache[word] = encoded;
            ids.AddRange(encoded);
        }
    }

    private int[] ApplyBpe(string word)
    {
        if (word.Length == 1)
            return _vocab.TryGetValue(word, out int single) ? [single] : [];

        var symbols = new List<string>(word.Length);
        foreach (char ch in word)
            symbols.Add(ch.ToString());

        while (symbols.Count > 1)
        {
            int bestRank = int.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < symbols.Count - 1; i++)
            {
                if (_merges.TryGetValue((symbols[i], symbols[i + 1]), out int r) && r < bestRank)
                {
                    bestRank = r;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0) break;

            string merged = symbols[bestIdx] + symbols[bestIdx + 1];
            symbols[bestIdx] = merged;
            symbols.RemoveAt(bestIdx + 1);
        }

        var result = new int[symbols.Count];
        int count = 0;
        foreach (var sym in symbols)
        {
            if (_vocab.TryGetValue(sym, out int id))
                result[count++] = id;
        }
        return count == result.Length ? result : result[..count];
    }

    public string Decode(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return "";
        var bytes = new List<byte>(ids.Count * 4);
        foreach (int id in ids)
        {
            if (_specialIds.Contains(id)) continue;
            if (!_idToToken.TryGetValue(id, out var tok)) continue;
            foreach (char ch in tok)
            {
                if (CharToByte.TryGetValue(ch, out byte b))
                    bytes.Add(b);
            }
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    public string DecodeDelta(IReadOnlyList<int> allIds, int alreadyShown)
    {
        var full = Decode(allIds);
        return full[Math.Min(alreadyShown, full.Length)..];
    }

    public void Dispose() { }

    private static char[] BuildByteToChar()
    {
        var table = new char[256];
        var used = new bool[256];
        for (int b = '!'; b <= '~'; b++) { table[b] = (char)b; used[b] = true; }
        for (int b = 0xA1; b <= 0xAC; b++) { table[b] = (char)b; used[b] = true; }
        for (int b = 0xAE; b <= 0xFF; b++) { table[b] = (char)b; used[b] = true; }

        int extra = 256;
        for (int b = 0; b < 256; b++)
        {
            if (!used[b])
                table[b] = (char)(extra++);
        }
        return table;
    }

    private static Dictionary<char, byte> BuildCharToByte(char[] byteToChar)
    {
        var map = new Dictionary<char, byte>(256);
        for (int b = 0; b < 256; b++)
            map[byteToChar[b]] = (byte)b;
        return map;
    }
}
