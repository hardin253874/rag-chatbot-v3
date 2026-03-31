namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Generates unique document chunk IDs following the pattern doc_{timestamp}_{index}.
/// </summary>
public static class DocumentIdGenerator
{
    /// <summary>
    /// Generates a document chunk ID.
    /// </summary>
    /// <param name="index">The chunk index within the batch.</param>
    /// <param name="timestampMs">Optional timestamp in milliseconds. Defaults to current UTC time.</param>
    public static string Generate(int index, long? timestampMs = null)
    {
        var timestamp = timestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"doc_{timestamp}_{index}";
    }
}
