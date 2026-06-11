using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using RagChatbot.Core.Configuration;
using RagChatbot.Core.Models;
using RagChatbot.Infrastructure.Chat;

namespace RagChatbot.Tests.Chat;

public class LlmServiceFactoryTests
{
    private static LlmServiceFactory CreateFactory(IHttpClientFactory? httpClientFactory = null) =>
        new(httpClientFactory ?? Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance);

    private static LlmProfile CreateProfile(string provider) => new()
    {
        Name = $"{provider}-profile",
        Provider = provider,
        BaseUrl = "https://example.com/v1",
        Model = "some-model",
        ApiKeyEnv = "RAGTEST_FACTORY_UNSET_KEY"
    };

    // --- Provider selection ---

    [Fact]
    public void Create_OpenAiProvider_ReturnsLlmService()
    {
        var service = CreateFactory().Create(CreateProfile("openai"));

        service.Should().BeOfType<LlmService>();
    }

    [Fact]
    public void Create_AnthropicProvider_ReturnsAnthropicLlmService()
    {
        var service = CreateFactory().Create(CreateProfile("anthropic"));

        service.Should().BeOfType<AnthropicLlmService>();
    }

    [Theory]
    [InlineData("OpenAI", typeof(LlmService))]
    [InlineData("ANTHROPIC", typeof(AnthropicLlmService))]
    public void Create_ProviderIsCaseInsensitive(string provider, Type expectedType)
    {
        var service = CreateFactory().Create(CreateProfile(provider));

        service.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Create_UnknownProvider_Throws()
    {
        var act = () => CreateFactory().Create(CreateProfile("gemini"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*gemini*");
    }

    // --- API key resolution (env[ApiKeyEnv] ?? env[ApiKeyFallbackEnv] ?? "") ---

    [Fact]
    public void ResolveApiKey_UsesPrimaryEnv_WhenSet()
    {
        const string primary = "RAGTEST_RAK_PRIMARY_1";
        const string fallback = "RAGTEST_RAK_FALLBACK_1";
        try
        {
            Environment.SetEnvironmentVariable(primary, "primary-key");
            Environment.SetEnvironmentVariable(fallback, "fallback-key");

            var key = LlmServiceFactory.ResolveApiKey(new LlmProfile
            {
                ApiKeyEnv = primary,
                ApiKeyFallbackEnv = fallback
            });

            key.Should().Be("primary-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(primary, null);
            Environment.SetEnvironmentVariable(fallback, null);
        }
    }

    [Fact]
    public void ResolveApiKey_FallsBack_WhenPrimaryUnset()
    {
        const string primary = "RAGTEST_RAK_PRIMARY_2";
        const string fallback = "RAGTEST_RAK_FALLBACK_2";
        try
        {
            Environment.SetEnvironmentVariable(fallback, "fallback-key");

            var key = LlmServiceFactory.ResolveApiKey(new LlmProfile
            {
                ApiKeyEnv = primary,
                ApiKeyFallbackEnv = fallback
            });

            key.Should().Be("fallback-key",
                because: "production rewrite relies on the OPENAI_API_KEY fallback when REWRITE_LLM_API_KEY is unset");
        }
        finally
        {
            Environment.SetEnvironmentVariable(fallback, null);
        }
    }

    [Fact]
    public void ResolveApiKey_FallsBack_WhenPrimaryIsWhitespace()
    {
        const string primary = "RAGTEST_RAK_PRIMARY_3";
        const string fallback = "RAGTEST_RAK_FALLBACK_3";
        try
        {
            Environment.SetEnvironmentVariable(primary, "   ");
            Environment.SetEnvironmentVariable(fallback, "fallback-key");

            var key = LlmServiceFactory.ResolveApiKey(new LlmProfile
            {
                ApiKeyEnv = primary,
                ApiKeyFallbackEnv = fallback
            });

            key.Should().Be("fallback-key",
                because: "AppConfig.Effective*ApiKey treats whitespace as unset — parity required");
        }
        finally
        {
            Environment.SetEnvironmentVariable(primary, null);
            Environment.SetEnvironmentVariable(fallback, null);
        }
    }

    [Fact]
    public void ResolveApiKey_ReturnsEmpty_WhenNeitherSet()
    {
        var key = LlmServiceFactory.ResolveApiKey(new LlmProfile
        {
            ApiKeyEnv = "RAGTEST_RAK_PRIMARY_4",
            ApiKeyFallbackEnv = "RAGTEST_RAK_FALLBACK_4"
        });

        key.Should().BeEmpty();
    }

    [Fact]
    public void ResolveApiKey_ReturnsEmpty_WhenPrimaryUnsetAndNoFallbackConfigured()
    {
        var key = LlmServiceFactory.ResolveApiKey(new LlmProfile
        {
            ApiKeyEnv = "RAGTEST_RAK_PRIMARY_5",
            ApiKeyFallbackEnv = null
        });

        key.Should().BeEmpty();
    }

    // --- End-to-end: created OpenAI service uses profile base URL + resolved key ---

    [Fact]
    public async Task Create_OpenAiService_UsesProfileBaseUrlAndResolvedKey()
    {
        const string envName = "RAGTEST_FACTORY_E2E_KEY";
        try
        {
            Environment.SetEnvironmentVariable(envName, "resolved-key");

            HttpRequestMessage? capturedRequest = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                        Encoding.UTF8, "application/json")
                });

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient("OpenAI"))
                .Returns(() => new HttpClient(handler.Object));

            var profile = new LlmProfile
            {
                Name = "custom-openai",
                Provider = "openai",
                BaseUrl = "https://custom.example.com/v1",
                Model = "custom-model",
                ApiKeyEnv = envName
            };

            var service = CreateFactory(httpClientFactory.Object).Create(profile);
            await service.ChatWithToolsAsync(
                new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
                new List<ToolDefinition>());

            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.ToString()
                .Should().Be("https://custom.example.com/v1/chat/completions",
                    because: "the profile base URL must override the shared OpenAI client base address");
            capturedRequest.Headers.Authorization!.Parameter.Should().Be("resolved-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }
}
