using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace RagChatbot.Infrastructure.DocumentProcessing;

/// <summary>
/// Converts PDF and Word (.docx) documents to Markdown text.
/// Uses PdfPig for PDF text extraction and DocumentFormat.OpenXml for Word.
/// </summary>
public static class DocumentConverter
{
    private const string ImageOnlyPdfMessage =
        "This PDF appears to be scanned/image-only. Please use a text-based PDF.";

    /// <summary>
    /// Extracts text from a PDF stream and returns it as Markdown (plain text per page,
    /// pages joined with a paragraph break / blank line).
    /// </summary>
    /// <param name="pdfStream">PDF file content as a stream. Must be readable.</param>
    /// <returns>Markdown-formatted text content of the PDF.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="pdfStream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// If the PDF contains no extractable text (scanned/image-only) or fails to parse.
    /// </exception>
    public static string ConvertPdfToMarkdown(Stream pdfStream)
    {
        if (pdfStream is null)
            throw new ArgumentNullException(nameof(pdfStream));

        var pageTexts = new List<string>();

        try
        {
            using var document = PdfDocument.Open(pdfStream);
            foreach (var page in document.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pageTexts.Add(text.Trim());
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Re-throw our own validation errors as-is.
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read PDF: {ex.Message}", ex);
        }

        if (pageTexts.Count == 0)
        {
            throw new InvalidOperationException(ImageOnlyPdfMessage);
        }

        // Pages joined with blank line for paragraph break.
        return string.Join("\n\n", pageTexts);
    }

    /// <summary>
    /// Converts a Word (.docx) document stream to Markdown.
    /// Detects heading styles (Heading1-4), list paragraphs, and bold/italic runs.
    /// </summary>
    /// <param name="docxStream">DOCX file content as a stream. Must be readable.</param>
    /// <returns>Markdown-formatted text content of the document.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="docxStream"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// If the file is not a valid OpenXml package (corrupted or wrong format).
    /// </exception>
    public static string ConvertDocxToMarkdown(Stream docxStream)
    {
        if (docxStream is null)
            throw new ArgumentNullException(nameof(docxStream));

        var output = new StringBuilder();

        try
        {
            using var doc = WordprocessingDocument.Open(docxStream, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body is null)
            {
                // Valid open but no body — return empty string.
                return string.Empty;
            }

            var firstParagraph = true;

            foreach (var paragraph in body.Elements<Paragraph>())
            {
                var paragraphMarkdown = ConvertParagraph(paragraph);

                // Skip truly empty paragraphs to avoid runaway blank lines.
                if (string.IsNullOrWhiteSpace(paragraphMarkdown))
                    continue;

                if (!firstParagraph)
                {
                    // Paragraph break (blank line) between paragraphs.
                    output.Append("\n\n");
                }
                output.Append(paragraphMarkdown);
                firstParagraph = false;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read DOCX: {ex.Message}", ex);
        }

        return output.ToString();
    }

    /// <summary>
    /// Converts a single Word paragraph (with its runs) to a Markdown line.
    /// </summary>
    private static string ConvertParagraph(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        // Build the run-level inline content first (bold/italic wrapping).
        var inline = new StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            inline.Append(ConvertRun(run));
        }

        // Empty paragraph — nothing to emit.
        if (inline.Length == 0)
            return string.Empty;

        var prefix = StylePrefix(styleId);
        return prefix + inline.ToString();
    }

    /// <summary>
    /// Maps a Word paragraph style id to a Markdown prefix.
    /// </summary>
    private static string StylePrefix(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return string.Empty;

        return styleId switch
        {
            "Heading1" => "# ",
            "Heading2" => "## ",
            "Heading3" => "### ",
            "Heading4" => "#### ",
            "ListParagraph" => "- ",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Converts a single run into Markdown, applying bold/italic wrapping.
    /// </summary>
    private static string ConvertRun(Run run)
    {
        // Concatenate all Text elements in the run (preserves spaces).
        var sb = new StringBuilder();
        foreach (var t in run.Elements<Text>())
        {
            sb.Append(t.Text);
        }

        var text = sb.ToString();
        if (text.Length == 0)
            return string.Empty;

        var rPr = run.RunProperties;
        var bold = rPr?.Bold is not null;
        var italic = rPr?.Italic is not null;

        // Bold takes the outer wrap so output is **... or **_..._** style.
        if (bold && italic)
            return $"***{text}***";
        if (bold)
            return $"**{text}**";
        if (italic)
            return $"*{text}*";
        return text;
    }
}
