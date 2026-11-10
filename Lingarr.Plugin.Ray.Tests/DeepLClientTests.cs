using System.Net;
using System.Text;
using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class DeepLClientTests
{
    [Fact]
    public async Task TranslateAsync_ReturnsTranslatedTextsInOrder()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"translations":[{"text":"Здравей"},{"text":"Свят"}]}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = new DeepLClient(new HttpClient(handler), "https://api.deepl.com", "test-key");

        var result = await client.TranslateAsync(["Hello", "World"], "EN", "BG", CancellationToken.None);

        Assert.Equal(["Здравей", "Свят"], result);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://api.deepl.com/v2/translate", handler.LastUri?.ToString());
        Assert.Equal("DeepL-Auth-Key test-key", handler.LastAuthorization);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsQuotaExceptionOn456()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage((HttpStatusCode)456)
        {
            Content = new StringContent("Quota exceeded", Encoding.UTF8, "text/plain")
        });
        var client = new DeepLClient(new HttpClient(handler), "https://api.deepl.com", "test-key");

        await Assert.ThrowsAsync<DeepLQuotaException>(() =>
            client.TranslateAsync(["Hello"], "EN", "BG", CancellationToken.None));
    }

    [Fact]
    public async Task GetUsageAsync_ReadsAnyLimitReached()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"character_count":500000,"character_limit":500000}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = new DeepLClient(new HttpClient(handler), "https://api.deepl.com", "test-key");

        var usage = await client.GetUsageAsync(CancellationToken.None);

        Assert.True(usage.AnyLimitReached);
        Assert.Equal(500000, usage.CharacterCount);
        Assert.Equal(500000, usage.CharacterLimit);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(_responder(request));
        }
    }
}
