namespace RagChatbot.Core.Configuration;

/// <summary>
/// Immutable registry of LLM profiles and interface bindings.
/// Built once at startup by the loader; resolution throws on unknown names
/// so misconfiguration fails fast with a descriptive message.
/// </summary>
public class LlmProfileRegistry
{
    private readonly Dictionary<string, LlmProfile> _profiles;
    private readonly Dictionary<string, InterfaceBinding> _bindings;

    public LlmProfileRegistry(
        IEnumerable<LlmProfile> profiles,
        IDictionary<string, InterfaceBinding> bindings)
    {
        _profiles = new Dictionary<string, LlmProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            _profiles[profile.Name] = profile;
        }

        _bindings = new Dictionary<string, InterfaceBinding>(bindings, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All registered profiles.</summary>
    public IReadOnlyCollection<LlmProfile> Profiles => _profiles.Values;

    /// <summary>
    /// Resolves a profile by name. Throws on unknown names.
    /// </summary>
    public LlmProfile Resolve(string name)
    {
        if (_profiles.TryGetValue(name, out var profile))
            return profile;

        throw new InvalidOperationException(
            $"Unknown LLM profile '{name}'. Known profiles: {string.Join(", ", _profiles.Keys)}");
    }

    /// <summary>
    /// Resolves an interface binding (e.g., "web", "mcp", "bot"). Throws on unknown names.
    /// </summary>
    public InterfaceBinding ResolveBinding(string interfaceName)
    {
        if (_bindings.TryGetValue(interfaceName, out var binding))
            return binding;

        throw new InvalidOperationException(
            $"Unknown interface binding '{interfaceName}'. Known bindings: {string.Join(", ", _bindings.Keys)}");
    }

    /// <summary>
    /// Returns true if a binding exists for the given interface name.
    /// </summary>
    public bool HasBinding(string interfaceName) => _bindings.ContainsKey(interfaceName);
}
