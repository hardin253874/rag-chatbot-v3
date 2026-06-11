using FluentAssertions;
using RagChatbot.Core.Configuration;
using RagChatbot.Infrastructure.Configuration;

namespace RagChatbot.Tests.Configuration;

public class LlmProfileLoaderTests
{
    private static AppConfig CreateAppConfig() => new()
    {
        LlmBaseUrl = "https://llm.example.com/v1",
        LlmModel = "main-model",
        RewriteLlmBaseUrl = "https://rewrite.example.com/v1",
        RewriteLlmModel = "rewrite-model"
    };

    [Fact]
    public void Build_SynthesizesDefaultProfile_FromAppConfig()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var profile = registry.Resolve("default");

        profile.Provider.Should().Be("openai");
        profile.BaseUrl.Should().Be("https://llm.example.com/v1");
        profile.Model.Should().Be("main-model");
        profile.ApiKeyEnv.Should().Be("LLM_API_KEY");
        profile.ApiKeyFallbackEnv.Should().Be("OPENAI_API_KEY",
            because: "the built-in must replicate AppConfig.EffectiveLlmApiKey fallback");
    }

    [Fact]
    public void Build_SynthesizesDefaultRewriteProfile_WithOpenAiKeyFallback()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var profile = registry.Resolve("default-rewrite");

        profile.Provider.Should().Be("openai");
        profile.BaseUrl.Should().Be("https://rewrite.example.com/v1");
        profile.Model.Should().Be("rewrite-model");
        profile.ApiKeyEnv.Should().Be("REWRITE_LLM_API_KEY");
        profile.ApiKeyFallbackEnv.Should().Be("OPENAI_API_KEY",
            because: "production does NOT set REWRITE_LLM_API_KEY — rewrite must fall back to OPENAI_API_KEY");
    }

    [Fact]
    public void Build_DefaultRewriteProfile_UsesAppConfigDefaults_WhenEnvNotSet()
    {
        // AppConfig defaults (no env overrides) — bound values, not raw env reads
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), new AppConfig());

        var profile = registry.Resolve("default-rewrite");

        profile.BaseUrl.Should().Be("https://api.openai.com/v1");
        profile.Model.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void Build_BindsWebInterface_ToDefaultProfiles()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var binding = registry.ResolveBinding("web");

        binding.AnswerProfile.Should().Be("default");
        binding.RewriteProfile.Should().Be("default-rewrite");
    }

    [Fact]
    public void Build_BindsMcpInterface_ToDefaultProfiles()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var binding = registry.ResolveBinding("mcp");

        binding.AnswerProfile.Should().Be("default");
        binding.RewriteProfile.Should().Be("default-rewrite");
    }

    [Fact]
    public void Build_MergesDeclaredProfiles_AlongsideBuiltIns()
    {
        var options = new LlmProfilesOptions
        {
            LlmProfiles = new List<LlmProfile>
            {
                new()
                {
                    Name = "claude-answer",
                    Provider = "anthropic",
                    BaseUrl = "https://api.anthropic.com",
                    Model = "claude-sonnet-4-6",
                    ApiKeyEnv = "ANTHROPIC_API_KEY"
                }
            }
        };

        var registry = LlmProfileLoader.Build(options, CreateAppConfig());

        registry.Resolve("claude-answer").Provider.Should().Be("anthropic");
        registry.Resolve("default").Provider.Should().Be("openai");
        registry.Resolve("default-rewrite").Should().NotBeNull();
    }

    [Fact]
    public void Build_DeclaredProfile_OverridesBuiltInByName()
    {
        var options = new LlmProfilesOptions
        {
            LlmProfiles = new List<LlmProfile>
            {
                new()
                {
                    Name = "default",
                    Provider = "anthropic",
                    BaseUrl = "https://api.anthropic.com",
                    Model = "claude-sonnet-4-6",
                    ApiKeyEnv = "ANTHROPIC_API_KEY"
                }
            }
        };

        var registry = LlmProfileLoader.Build(options, CreateAppConfig());

        var profile = registry.Resolve("default");
        profile.Provider.Should().Be("anthropic");
        profile.Model.Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public void Build_DeclaredBinding_AddsBotInterface()
    {
        var options = new LlmProfilesOptions
        {
            InterfaceBindings = new Dictionary<string, InterfaceBinding>
            {
                ["bot"] = new() { AnswerProfile = "claude-answer", RewriteProfile = "claude-rewrite" }
            }
        };

        var registry = LlmProfileLoader.Build(options, CreateAppConfig());

        registry.HasBinding("bot").Should().BeTrue();
        var binding = registry.ResolveBinding("bot");
        binding.AnswerProfile.Should().Be("claude-answer");
        binding.RewriteProfile.Should().Be("claude-rewrite");

        // built-in bindings unaffected
        registry.ResolveBinding("web").AnswerProfile.Should().Be("default");
    }

    [Fact]
    public void Build_DeclaredBinding_OverridesBuiltInBinding()
    {
        var options = new LlmProfilesOptions
        {
            InterfaceBindings = new Dictionary<string, InterfaceBinding>
            {
                ["web"] = new() { AnswerProfile = "custom", RewriteProfile = "custom-rewrite" }
            }
        };

        var registry = LlmProfileLoader.Build(options, CreateAppConfig());

        registry.ResolveBinding("web").AnswerProfile.Should().Be("custom");
    }

    [Fact]
    public void Resolve_UnknownProfile_Throws()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var act = () => registry.Resolve("does-not-exist");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does-not-exist*");
    }

    [Fact]
    public void ResolveBinding_UnknownInterface_Throws()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        var act = () => registry.ResolveBinding("teams");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*teams*");
    }

    [Fact]
    public void HasBinding_ReturnsFalse_ForUnknownInterface()
    {
        var registry = LlmProfileLoader.Build(new LlmProfilesOptions(), CreateAppConfig());

        registry.HasBinding("bot").Should().BeFalse();
        registry.HasBinding("web").Should().BeTrue();
    }

    [Fact]
    public void Build_IgnoresProfilesWithEmptyNames()
    {
        var options = new LlmProfilesOptions
        {
            LlmProfiles = new List<LlmProfile> { new() { Name = "" } }
        };

        var registry = LlmProfileLoader.Build(options, CreateAppConfig());

        registry.Profiles.Should().HaveCount(2); // only the two built-ins
    }
}
