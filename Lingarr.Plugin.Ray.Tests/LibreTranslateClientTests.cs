using System.Net;
using System.Text;
using System.Text.Json;
using Lingarr.Plugin.Ray.LibreTranslate;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class LibreTranslateClientTests
{
    [Fact]
    public async Task TranslateAsync_ReturnsTranslationsInOrderAndNormalizesLanguages()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"translatedText":["Здравей","Свят"]}""");
        });
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001/");

        var result = await client.TranslateAsync(
            ["Hello", "World"], "EN_us", "BG", CancellationToken.None);

        Assert.Equal(["Здравей", "Свят"], result);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("http://localhost:5001/translate", handler.LastUri?.ToString());
        using var request = JsonDocument.Parse(body!);
        Assert.Equal(["Hello", "World"],
            request.RootElement.GetProperty("q").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty).ToArray());
        Assert.Equal("en", request.RootElement.GetProperty("source").GetString());
        Assert.Equal("bg", request.RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task TranslateAsync_PreservesBlankCuesWithoutSendingThem()
    {
        string? body = null;
        var handler = new StubHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{"translatedText":["Здравей"]}""");
        });
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001");

        var result = await client.TranslateAsync(
            ["Hello", "  ", ""], "en", "bg", CancellationToken.None);

        Assert.Equal(["Здравей", "  ", ""], result);
        using var request = JsonDocument.Parse(body!);
        Assert.Equal(1, request.RootElement.GetProperty("q").GetArrayLength());
    }

    [Fact]
    public async Task TranslateAsync_SplitsLargePayloads()
    {
        var handler = new StubHandler(request =>
        {
            using var document = JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var count = document.RootElement.GetProperty("q").GetArrayLength();
            var translations = string.Join(",", Enumerable.Repeat("\"x\"", count));
            return JsonResponse("{\"translatedText\":[" + translations + "]}");
        });
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001");
        var texts = Enumerable.Repeat(new string('a', 20_000), 5).ToArray();

        var result = await client.TranslateAsync(texts, "en", "bg", CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.True(handler.CallCount > 1);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsWhenResponseHasWrongTranslationCount()
    {
        var handler = new StubHandler(_ =>
            JsonResponse("""{"translatedText":["Здравей"]}"""));
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.TranslateAsync(["Hello", "World"], "en", "bg", CancellationToken.None));

        Assert.Contains("returned 1 translations for 2 texts", exception.Message);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsWhenResponseContainsNonTextTranslation()
    {
        var handler = new StubHandler(_ =>
            JsonResponse("""{"translatedText":[null]}"""));
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.TranslateAsync(["Hello"], "en", "bg", CancellationToken.None));

        Assert.Contains("non-text translation", exception.Message);
    }

    [Fact]
    public async Task TranslateAsync_ReportsHttpErrors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("temporarily unavailable")
        });
        var client = new LibreTranslateClient(new HttpClient(handler), "http://localhost:5001");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.TranslateAsync(["Hello"], "en", "bg", CancellationToken.None));

        Assert.Contains("HTTP 503", exception.Message);
        Assert.Contains("temporarily unavailable", exception.Message);
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }
}
