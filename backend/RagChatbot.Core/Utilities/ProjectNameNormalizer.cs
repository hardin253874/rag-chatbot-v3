using System.Text.RegularExpressions;

namespace RagChatbot.Core.Utilities;

/// <summary>
/// Normalizes project name strings to a canonical uppercase-dash format.
/// </summary>
public static class ProjectNameNormalizer
{
    /// <summary>
    /// Normalizes a project name: trims whitespace, collapses space-dash patterns,
    /// replaces spaces with dashes, collapses multiple dashes, trims dashes, uppercases.
    /// Returns empty string for null/empty/whitespace input.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var result = input.Trim();
        // Collapse "space-dash-space" and variants into single dash
        result = Regex.Replace(result, @"\s*-\s*", "-");
        // Replace remaining spaces with dash
        result = Regex.Replace(result, @"\s+", "-");
        // Collapse multiple dashes
        result = Regex.Replace(result, @"-{2,}", "-");
        // Trim leading/trailing dashes
        result = result.Trim('-');
        // Uppercase
        result = result.ToUpperInvariant();
        return result;
    }
}
