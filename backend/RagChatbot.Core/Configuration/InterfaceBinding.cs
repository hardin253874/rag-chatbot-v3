namespace RagChatbot.Core.Configuration;

/// <summary>
/// Maps a shell interface (web, mcp, bot) to the LLM profiles it uses
/// for answer generation and query rewriting.
/// </summary>
public class InterfaceBinding
{
    /// <summary>Profile name used for answer generation.</summary>
    public string AnswerProfile { get; set; } = string.Empty;

    /// <summary>Profile name used for query rewriting.</summary>
    public string RewriteProfile { get; set; } = string.Empty;
}
