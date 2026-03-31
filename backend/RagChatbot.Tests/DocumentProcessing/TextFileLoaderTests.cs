using FluentAssertions;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class TextFileLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string CreateTempFile(string content, string extension = ".txt")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task LoadAsync_ReadsTxtFile_ReturnsDocumentWithContent()
    {
        var loader = new TextFileLoader();
        var content = "Hello, this is a test file.\nWith multiple lines.";
        var path = CreateTempFile(content);

        var doc = await loader.LoadAsync(path, "test.txt");

        doc.PageContent.Should().Be(content);
    }

    [Fact]
    public async Task LoadAsync_SetsSourceToOriginalFileName()
    {
        var loader = new TextFileLoader();
        var path = CreateTempFile("content");

        var doc = await loader.LoadAsync(path, "my-document.txt");

        doc.Metadata.Should().ContainKey("source");
        doc.Metadata["source"].Should().Be("my-document.txt");
    }

    [Fact]
    public async Task LoadAsync_ReadsMdFile_ReturnsContent()
    {
        var loader = new TextFileLoader();
        var content = "# Heading\n\nSome markdown content.";
        var path = CreateTempFile(content, ".md");

        var doc = await loader.LoadAsync(path, "readme.md");

        doc.PageContent.Should().Be(content);
        doc.Metadata["source"].Should().Be("readme.md");
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnEmptyFilePath()
    {
        var loader = new TextFileLoader();

        var act = () => loader.LoadAsync("", "file.txt");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnEmptyOriginalFileName()
    {
        var loader = new TextFileLoader();
        var path = CreateTempFile("content");

        var act = () => loader.LoadAsync(path, "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnNonExistentFile()
    {
        var loader = new TextFileLoader();

        var act = () => loader.LoadAsync("/nonexistent/path.txt", "file.txt");

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadAsync_HandlesUtf8Content()
    {
        var loader = new TextFileLoader();
        var content = "Unicode content: cafe\u0301, na\u00efve, r\u00e9sum\u00e9";
        var path = CreateTempFile(content);

        var doc = await loader.LoadAsync(path, "unicode.txt");

        doc.PageContent.Should().Be(content);
    }
}
