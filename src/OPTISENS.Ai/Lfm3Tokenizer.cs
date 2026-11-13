using Microsoft.ML.OnnxRuntimeGenAI;

namespace OPTISENS.Ai;

public sealed class Lfm3Tokenizer : IDisposable
{
    private readonly Model _model;
    private readonly Tokenizer _tok;

    public Lfm3Tokenizer(string modelDir)
    {
        _model = new Model(modelDir);
        _tok = new Tokenizer(_model);
    }

    public int[] Encode(string text)
    {
        using var seq = _tok.Encode(text);
        return seq[0].ToArray();
    }

    public string Decode(IReadOnlyList<int> ids)
    {
        var arr = ids as int[] ?? ids.ToArray();
        return _tok.Decode(arr);
    }

    public string DecodeDelta(IReadOnlyList<int> allIds, int alreadyShown)
    {
        var full = Decode(allIds);
        return full[Math.Min(alreadyShown, full.Length)..];
    }

    public void Dispose()
    {
        _tok.Dispose();
        _model.Dispose();
    }
}
