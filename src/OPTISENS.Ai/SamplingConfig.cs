using OPTISENS.Core.Config;

namespace OPTISENS.Ai;

public sealed record SamplingConfig(
    double Temperature = Constants.AiTemperature,
    int TopK = Constants.AiTopK,
    double RepetitionPenalty = Constants.AiRepetitionPenalty,
    int? Seed = null)
{
    public static SamplingConfig Default { get; } = new();
}

public static class Sampler
{
    public static int Sample(
        ReadOnlySpan<float> logits,
        IReadOnlyList<int> allIds,
        IReadOnlyList<int> generated,
        SamplingConfig cfg)
    {
        int vocab = logits.Length;
        if (vocab == 0) throw new ArgumentException("empty logits");

        var work = new float[vocab];
        logits.CopyTo(work);

        // (a) repetition penalty over ids U generated
        if (Math.Abs(cfg.RepetitionPenalty - 1.0) > 1e-6)
        {
            float pen = (float)cfg.RepetitionPenalty;
            for (int i = 0; i < allIds.Count; i++)
            {
                int id = allIds[i];
                if ((uint)id < (uint)vocab)
                    work[id] = work[id] > 0 ? work[id] / pen : work[id] * pen;
            }
        }

        // Greedy path (Temperature <= 0)
        if (cfg.Temperature <= 0)
        {
            int bestId = 0;
            float bestVal = work[0];
            for (int i = 1; i < vocab; i++)
            {
                if (work[i] > bestVal) { bestVal = work[i]; bestId = i; }
            }
            return bestId;
        }

        // (b) temperature divide
        float invT = (float)(1.0 / cfg.Temperature);
        for (int i = 0; i < vocab; i++) work[i] *= invT;

        // (c) keep top-K
        int k = Math.Clamp(cfg.TopK, 1, vocab);
        var topIdx = new int[k];
        var topVal = new float[k];
        Array.Fill(topVal, float.NegativeInfinity);

        for (int i = 0; i < vocab; i++)
        {
            float v = work[i];
            if (v <= topVal[k - 1]) continue;
            int pos = k - 1;
            while (pos > 0 && v > topVal[pos - 1])
            {
                topVal[pos] = topVal[pos - 1];
                topIdx[pos] = topIdx[pos - 1];
                pos--;
            }
            topVal[pos] = v;
            topIdx[pos] = i;
        }

        // (d) softmax over top-K
        float maxV = topVal[0];
        double sum = 0;
        var probs = new double[k];
        for (int i = 0; i < k; i++)
        {
            probs[i] = Math.Exp(topVal[i] - maxV);
            sum += probs[i];
        }

        // (e) sample
        var rng = cfg.Seed.HasValue ? new Random(cfg.Seed.Value + generated.Count) : Random.Shared;
        double r = rng.NextDouble() * sum;
        double acc = 0;
        for (int i = 0; i < k; i++)
        {
            acc += probs[i];
            if (r <= acc) return topIdx[i];
        }
        return topIdx[0];
    }
}
