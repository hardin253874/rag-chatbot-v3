namespace RagChatbot.Infrastructure.Chat;

/// <summary>
/// IHttpClientFactory decorator that overrides the BaseAddress of created clients
/// with a fixed base URL. Used by profile-based LlmService instances so the shared
/// "OpenAI" named client registration (whose BaseAddress comes from the global
/// AppConfig) is not affected.
/// </summary>
public sealed class ProfileBaseUrlHttpClientFactory : IHttpClientFactory
{
    private readonly IHttpClientFactory _inner;
    private readonly Uri _baseAddress;

    public ProfileBaseUrlHttpClientFactory(IHttpClientFactory inner, string baseUrl)
    {
        _inner = inner;
        _baseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public HttpClient CreateClient(string name)
    {
        var client = _inner.CreateClient(name);
        client.BaseAddress = _baseAddress;
        return client;
    }
}
