using RagChatbot.Core.Configuration;

namespace RagChatbot.Infrastructure.Configuration;

/// <summary>
/// Builds the LlmProfileRegistry by synthesizing built-in profiles/bindings from
/// the already-bound AppConfig (env is the source of truth, defaults applied),
/// then merging profiles and bindings declared in appsettings.json on top.
///
/// CRITICAL backward-compat: the built-in profiles replicate the AppConfig
/// Effective*ApiKey fallback chain via ApiKeyFallbackEnv. In production,
/// REWRITE_LLM_API_KEY is NOT set, so the rewrite profile MUST fall back
/// to OPENAI_API_KEY — exactly like AppConfig.EffectiveRewriteLlmApiKey.
/// </summary>
public static class LlmProfileLoader
{
    public const string DefaultProfileName = "default";
    public const string DefaultRewriteProfileName = "default-rewrite";

    public static LlmProfileRegistry Build(LlmProfilesOptions options, AppConfig config)
    {
        // Synthesized built-ins — baseUrl/model come from the bound AppConfig
        // (which already has env values + defaults applied), NOT raw env reads.
        var profiles = new Dictionary<string, LlmProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultProfileName] = new LlmProfile
            {
                Name = DefaultProfileName,
                Provider = "openai",
                BaseUrl = config.LlmBaseUrl,
                Model = config.LlmModel,
                ApiKeyEnv = "LLM_API_KEY",
                ApiKeyFallbackEnv = "OPENAI_API_KEY"
            },
            [DefaultRewriteProfileName] = new LlmProfile
            {
                Name = DefaultRewriteProfileName,
                Provider = "openai",
                BaseUrl = config.RewriteLlmBaseUrl,
                Model = config.RewriteLlmModel,
                ApiKeyEnv = "REWRITE_LLM_API_KEY",
                ApiKeyFallbackEnv = "OPENAI_API_KEY"
            }
        };

        // Merge declared profiles — override built-ins by name.
        foreach (var profile in options.LlmProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                continue;

            profiles[profile.Name] = profile;
        }

        // Built-in bindings: web and mcp keep today's env-driven behavior.
        var bindings = new Dictionary<string, InterfaceBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["web"] = new InterfaceBinding
            {
                AnswerProfile = DefaultProfileName,
                RewriteProfile = DefaultRewriteProfileName
            },
            ["mcp"] = new InterfaceBinding
            {
                AnswerProfile = DefaultProfileName,
                RewriteProfile = DefaultRewriteProfileName
            }
        };

        // Merge declared bindings — override built-ins by interface name.
        foreach (var (interfaceName, binding) in options.InterfaceBindings)
        {
            if (string.IsNullOrWhiteSpace(interfaceName))
                continue;

            bindings[interfaceName] = binding;
        }

        return new LlmProfileRegistry(profiles.Values, bindings);
    }
}
