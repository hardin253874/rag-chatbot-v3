using RagChatbot.Core.Models;

namespace RagChatbot.Core.Interfaces;

/// <summary>
/// Interface for tools that can be invoked by the agentic RAG pipeline.
/// Each tool wraps an existing service and formats results for LLM consumption.
/// </summary>
public interface IAgentTool
{
    /// <summary>The tool name as registered with the LLM.</summary>
    string Name { get; }

    /// <summary>The tool definition for the LLM function calling schema.</summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Executes the tool with the given JSON arguments and returns a string result.
    /// </summary>
    /// <param name="argumentsJson">JSON string of tool arguments from the LLM.</param>
    /// <returns>A formatted string result for the LLM context.</returns>
    Task<string> ExecuteAsync(string argumentsJson);
}
