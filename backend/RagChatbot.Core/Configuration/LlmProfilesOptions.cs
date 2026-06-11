namespace RagChatbot.Core.Configuration;

/// <summary>
/// Raw configuration shape for the LLM profile registry, bound from
/// the appsettings.json root sections "LlmProfiles" and "InterfaceBindings".
/// </summary>
public class LlmProfilesOptions
{
    /// <summary>Profiles declared in configuration (merged over the synthesized built-ins).</summary>
    public List<LlmProfile> LlmProfiles { get; set; } = new();

    /// <summary>Interface bindings declared in configuration (merged over the synthesized built-ins).</summary>
    public Dictionary<string, InterfaceBinding> InterfaceBindings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
