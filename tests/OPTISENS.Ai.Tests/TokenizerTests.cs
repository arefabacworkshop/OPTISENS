using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public sealed class TokenizerTests
{
    [Fact]
    public void EncodeDecode_HandlesBpeAndSpecialTokens()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"optisens-tok-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "tokenizer.json"), """
            {
              "added_tokens": [
                { "id": 100, "content": "<|im_start|>", "special": true },
                { "id": 101, "content": "<|im_end|>", "special": true }
              ],
              "model": {
                "type": "BPE",
                "vocab": {
                  "h": 1, "i": 2, "hi": 3, "Ġ": 4, "!": 5
                },
                "merges": [ "h i" ]
              }
            }
            """);

            using var tok = new Lfm3Tokenizer(dir);
            int[] ids = tok.Encode("<|im_start|>hi!<|im_end|>");
            Assert.Equal([100, 3, 5, 101], ids);
            Assert.Equal("hi!", tok.Decode(ids));
            Assert.Equal("!", tok.DecodeDelta(ids, 2));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
