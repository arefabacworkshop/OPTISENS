using System.Net;
using System.Text;
using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class OpenAiCoachSessionTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();

        public List<string> RequestBodies { get; } = new();

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handlers.Enqueue(handler);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return _handlers.Dequeue()(request);
        }
    }

    [Fact]
    public async Task AskAsync_StreamsSseTokens_AndExecutesToolCalls()
    {
        var handler = new StubHttpMessageHandler();

        // Turn 1: stream a tool call in chunks
        handler.Enqueue(_ =>
        {
            string sse =
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"adjust_sensitivity\",\"arguments\":\"{\\\"direction\\\":\\\"slower\\\",\"}}]}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"percentage\\\":10}\"}}]}}]}\n\n" +
                "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        // Turn 2: stream final prose reply
        handler.Enqueue(_ =>
        {
            string sse =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Adjusted \"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"10% slower.\"}}]}\n\n" +
                "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        using var http = new HttpClient(handler);
        var dispatcher = new ToolDispatcher(initialCm360: 30.0, dpi: 800, boundsLoCm360: 15.0, boundsHiCm360: 60.0);
        double? updatedCm360 = null;
        dispatcher.Cm360Changed += cm => updatedCm360 = cm;

        using var session = new OpenAiCoachSession(
            "https://api.example.com/v1/",
            "sk-test",
            "gpt-4o-mini",
            dispatcher,
            systemPrompt: "You are a coach.",
            httpClient: http);

        var observedCalls = new List<ToolCallRecord>();
        session.ToolCallObserved += c => observedCalls.Add(c);

        var streamed = new StringBuilder();
        string reply = await session.AskAsync("make it 10% slower", tok => streamed.Append(tok), CancellationToken.None);

        Assert.Equal("Adjusted 10% slower.", reply);
        Assert.Equal("Adjusted 10% slower.", streamed.ToString());
        Assert.Single(observedCalls);
        Assert.Equal("adjust_sensitivity", observedCalls[0].Name);
        Assert.NotNull(updatedCm360);
        Assert.Equal(33.0, updatedCm360!.Value, precision: 2);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("\"role\":\"tool\"", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsSuccessAndFailure()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}", Encoding.UTF8, "application/json")
        });
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Invalid API key", Encoding.UTF8, "text/plain")
        });

        using var http = new HttpClient(handler);
        var ok = await OpenAiCoachSession.TestConnectionAsync("http://localhost:11434/v1", null, "llama3.2", CancellationToken.None, http);
        Assert.True(ok.Ok);
        Assert.Contains("Connected", ok.Message);

        var fail = await OpenAiCoachSession.TestConnectionAsync("https://api.openai.com/v1", "bad", "gpt-4o-mini", CancellationToken.None, http);
        Assert.False(fail.Ok);
        Assert.Contains("401", fail.Message);
    }
}
