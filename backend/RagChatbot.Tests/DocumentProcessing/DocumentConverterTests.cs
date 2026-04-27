using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using RagChatbot.Infrastructure.DocumentProcessing;
using UglyToad.PdfPig.Writer;

namespace RagChatbot.Tests.DocumentProcessing;

public class DocumentConverterTests
{
    // ---------------------------------------------------------------------
    // Helpers — generate PDF and DOCX fixtures programmatically in memory.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal text-based PDF byte array using PdfPig's PdfDocumentBuilder.
    /// Each entry in <paramref name="pageTexts"/> becomes one page with that text.
    /// </summary>
    private static byte[] BuildTextPdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        // Use a built-in standard 14 font to avoid needing a TTF on disk.
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            // A4 page (~595x842 points).
            var page = builder.AddPage(595, 842);
            // Position near top-left in PDF coordinate space (origin bottom-left).
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 800), font);
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds an empty (image-only) PDF — pages with no text content at all.
    /// </summary>
    private static byte[] BuildEmptyPdf(int pageCount = 1)
    {
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            builder.AddPage(595, 842);
        }
        return builder.Build();
    }

    /// <summary>
    /// Builds a minimal DOCX byte array with the supplied paragraphs.
    /// Each tuple = (text, optional style id e.g. "Heading1", optional bold, optional italic).
    /// </summary>
    private static byte[] BuildDocx(IEnumerable<DocxParagraphSpec> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new Body());

            foreach (var spec in paragraphs)
            {
                var p = new Paragraph();

                // Apply paragraph style if specified.
                if (!string.IsNullOrEmpty(spec.StyleId))
                {
                    var pPr = new ParagraphProperties();
                    pPr.AppendChild(new ParagraphStyleId { Val = spec.StyleId });
                    p.AppendChild(pPr);
                }

                var run = new Run();
                if (spec.Bold || spec.Italic)
                {
                    var rPr = new RunProperties();
                    if (spec.Bold) rPr.AppendChild(new Bold());
                    if (spec.Italic) rPr.AppendChild(new Italic());
                    run.AppendChild(rPr);
                }
                run.AppendChild(new Text(spec.Text) { Space = SpaceProcessingModeValues.Preserve });
                p.AppendChild(run);
                body.AppendChild(p);
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private record DocxParagraphSpec(string Text, string? StyleId = null, bool Bold = false, bool Italic = false);

    // ---------------------------------------------------------------------
    // PDF tests
    // ---------------------------------------------------------------------

    [Fact]
    public void ConvertPdfToMarkdown_TextBasedPdf_ReturnsContent()
    {
        // Arrange
        var pdfBytes = BuildTextPdf("Hello World from PDF");
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var markdown = DocumentConverter.ConvertPdfToMarkdown(stream);

        // Assert
        markdown.Should().Contain("Hello World from PDF");
    }

    [Fact]
    public void ConvertPdfToMarkdown_EmptyPdf_ThrowsInvalidOperationException()
    {
        // Arrange — a PDF with pages but no text content (simulates scanned/image-only).
        var pdfBytes = BuildEmptyPdf(pageCount: 1);
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var act = () => DocumentConverter.ConvertPdfToMarkdown(stream);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*scanned*image-only*");
    }

    [Fact]
    public void ConvertPdfToMarkdown_MultiPagePdf_JoinsPagesWithParagraphBreaks()
    {
        // Arrange
        var pdfBytes = BuildTextPdf("Page one content", "Page two content");
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var markdown = DocumentConverter.ConvertPdfToMarkdown(stream);

        // Assert
        markdown.Should().Contain("Page one content");
        markdown.Should().Contain("Page two content");
        // A paragraph break (blank line / double newline) separates pages.
        markdown.Should().MatchRegex(@"Page one content.*\n\s*\n.*Page two content");
    }

    // ---------------------------------------------------------------------
    // DOCX tests
    // ---------------------------------------------------------------------

    [Fact]
    public void ConvertDocxToMarkdown_SimpleParagraphs_ReturnsPlainText()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("First paragraph."),
            new DocxParagraphSpec("Second paragraph.")
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("First paragraph.");
        markdown.Should().Contain("Second paragraph.");
        markdown.Should().MatchRegex(@"First paragraph\..*\n\s*\n.*Second paragraph\.");
    }

    [Fact]
    public void ConvertDocxToMarkdown_Heading1Paragraph_GetsHashPrefix()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("My Heading", StyleId: "Heading1")
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("# My Heading");
    }

    [Fact]
    public void ConvertDocxToMarkdown_Heading2AndHeading3_GetCorrectPrefixes()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("Section Title", StyleId: "Heading2"),
            new DocxParagraphSpec("Subsection Title", StyleId: "Heading3")
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("## Section Title");
        markdown.Should().Contain("### Subsection Title");
    }

    [Fact]
    public void ConvertDocxToMarkdown_BoldRuns_WrappedWithDoubleAsterisks()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("Important word", Bold: true)
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("**Important word**");
    }

    [Fact]
    public void ConvertDocxToMarkdown_ItalicRuns_WrappedWithSingleAsterisks()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("Emphasised text", Italic: true)
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("*Emphasised text*");
        // But not double asterisks (it isn't bold).
        markdown.Should().NotContain("**Emphasised text**");
    }

    [Fact]
    public void ConvertDocxToMarkdown_ListParagraphs_GetDashPrefix()
    {
        // Arrange
        var docxBytes = BuildDocx(new[]
        {
            new DocxParagraphSpec("First item", StyleId: "ListParagraph"),
            new DocxParagraphSpec("Second item", StyleId: "ListParagraph")
        });
        using var stream = new MemoryStream(docxBytes);

        // Act
        var markdown = DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        markdown.Should().Contain("- First item");
        markdown.Should().Contain("- Second item");
    }

    [Fact]
    public void ConvertDocxToMarkdown_CorruptedFile_ThrowsInvalidOperationException()
    {
        // Arrange — random bytes are not a valid OpenXml package.
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0xFF, 0xFE };
        using var stream = new MemoryStream(garbage);

        // Act
        var act = () => DocumentConverter.ConvertDocxToMarkdown(stream);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
