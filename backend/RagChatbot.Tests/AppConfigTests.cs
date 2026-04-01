using FluentAssertions;
using RagChatbot.Core.Configuration;

namespace RagChatbot.Tests;

public class AppConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new AppConfig();

        config.Port.Should().Be(3010);
        config.RewriteLlmBaseUrl.Should().Be("https://api.openai.com/v1");
        config.RewriteLlmModel.Should().Be("gpt-4o-mini");
        config.ChunkSize.Should().Be(1000);
        config.ChunkOverlap.Should().Be(100);
    }

    [Fact]
    public void EffectiveRewriteLlmApiKey_FallsBackToOpenAiApiKey()
    {
        var config = new AppConfig
        {
            OpenAiApiKey = "openai-key",
            RewriteLlmApiKey = ""
        };

        config.EffectiveRewriteLlmApiKey.Should().Be("openai-key");
    }

    [Fact]
    public void EffectiveRewriteLlmApiKey_UsesRewriteKeyWhenSet()
    {
        var config = new AppConfig
        {
            OpenAiApiKey = "openai-key",
            RewriteLlmApiKey = "rewrite-key"
        };

        config.EffectiveRewriteLlmApiKey.Should().Be("rewrite-key");
    }

    [Fact]
    public void FromEnvironment_ReadsEnvironmentVariables()
    {
        // Arrange — set env vars for this test
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-openai-key");
        Environment.SetEnvironmentVariable("PINECONE_API_KEY", "test-pinecone-key");
        Environment.SetEnvironmentVariable("PORT", "4000");
        Environment.SetEnvironmentVariable("CHUNK_SIZE", "500");
        Environment.SetEnvironmentVariable("CHUNK_OVERLAP", "50");
        Environment.SetEnvironmentVariable("REWRITE_LLM_BASE_URL", "https://custom.api/v1");
        Environment.SetEnvironmentVariable("REWRITE_LLM_MODEL", "custom-model");
        Environment.SetEnvironmentVariable("REWRITE_LLM_API_KEY", "test-rewrite-key");

        try
        {
            var config = AppConfig.FromEnvironment();

            config.OpenAiApiKey.Should().Be("test-openai-key");
            config.PineconeApiKey.Should().Be("test-pinecone-key");
            config.Port.Should().Be(4000);
            config.ChunkSize.Should().Be(500);
            config.ChunkOverlap.Should().Be(50);
            config.RewriteLlmBaseUrl.Should().Be("https://custom.api/v1");
            config.RewriteLlmModel.Should().Be("custom-model");
            config.RewriteLlmApiKey.Should().Be("test-rewrite-key");
        }
        finally
        {
            // Clean up
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("PINECONE_API_KEY", null);
            Environment.SetEnvironmentVariable("PORT", null);
            Environment.SetEnvironmentVariable("CHUNK_SIZE", null);
            Environment.SetEnvironmentVariable("CHUNK_OVERLAP", null);
            Environment.SetEnvironmentVariable("REWRITE_LLM_BASE_URL", null);
            Environment.SetEnvironmentVariable("REWRITE_LLM_MODEL", null);
            Environment.SetEnvironmentVariable("REWRITE_LLM_API_KEY", null);
        }
    }

    [Fact]
    public void FromEnvironment_UsesDefaults_WhenEnvVarsNotSet()
    {
        // Ensure vars are not set
        Environment.SetEnvironmentVariable("PORT", null);
        Environment.SetEnvironmentVariable("CHUNK_SIZE", null);
        Environment.SetEnvironmentVariable("CHUNK_OVERLAP", null);
        Environment.SetEnvironmentVariable("REWRITE_LLM_BASE_URL", null);
        Environment.SetEnvironmentVariable("REWRITE_LLM_MODEL", null);

        var config = AppConfig.FromEnvironment();

        config.Port.Should().Be(3010);
        config.ChunkSize.Should().Be(1000);
        config.ChunkOverlap.Should().Be(100);
        config.RewriteLlmBaseUrl.Should().Be("https://api.openai.com/v1");
        config.RewriteLlmModel.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void FromEnvironment_HandlesInvalidPortGracefully()
    {
        Environment.SetEnvironmentVariable("PORT", "not-a-number");

        try
        {
            var config = AppConfig.FromEnvironment();
            config.Port.Should().Be(3010, because: "invalid PORT should fall back to default");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORT", null);
        }
    }

    [Fact]
    public void LlmBaseUrl_DefaultsToOpenAi()
    {
        var config = new AppConfig();
        config.LlmBaseUrl.Should().Be("https://api.openai.com/v1");
    }

    [Fact]
    public void LlmModel_DefaultsToGpt4oMini()
    {
        var config = new AppConfig();
        config.LlmModel.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void EffectiveLlmApiKey_FallsBackToOpenAiApiKey_WhenLlmApiKeyEmpty()
    {
        var config = new AppConfig { OpenAiApiKey = "openai-key", LlmApiKey = "" };
        config.EffectiveLlmApiKey.Should().Be("openai-key");
    }

    [Fact]
    public void EffectiveLlmApiKey_UsesLlmApiKey_WhenSet()
    {
        var config = new AppConfig { OpenAiApiKey = "openai-key", LlmApiKey = "custom-key" };
        config.EffectiveLlmApiKey.Should().Be("custom-key");
    }
}
