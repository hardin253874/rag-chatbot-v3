using System.Text.RegularExpressions;
using HtmlAgilityPack;
using RagChatbot.Core.Interfaces;
using RagChatbot.Core.Models;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Loads a web page by fetching its HTML and extracting visible text content.
/// Strips scripts, styles, and non-content elements.
/// </summary>
public partial class WebPageLoader : IUrlLoader
{
    private readonly HttpClient _httpClient;

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleWhitespaceRegex();

    public WebPageLoader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<Document> LoadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must not be empty.", nameof(url));

        var html = await _httpClient.GetStringAsync(url);
        var text = ExtractVisibleText(html);

        return new Document
        {
            PageContent = text,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = url
            }
        };
    }

    /// <summary>
    /// Extracts visible text from HTML, stripping scripts, styles, and tags.
    /// Exposed as internal for testing.
    /// </summary>
    internal static string ExtractVisibleText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script, style, nav, header, footer elements
        var nodesToRemove = doc.DocumentNode
            .SelectNodes("//script|//style|//nav|//header|//footer|//noscript|//svg")
            ?.ToList();

        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        // Get inner text (HtmlAgilityPack decodes HTML entities)
        var text = doc.DocumentNode.InnerText;

        // Decode any remaining HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Normalise whitespace: collapse multiple spaces/newlines
        text = MultipleWhitespaceRegex().Replace(text, " ");

        return text.Trim();
    }
}
