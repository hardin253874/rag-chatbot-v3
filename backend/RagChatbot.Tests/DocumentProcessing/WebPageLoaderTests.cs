using System.Net;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagChatbot.Infrastructure.DocumentProcessing;

namespace RagChatbot.Tests.DocumentProcessing;

public class WebPageLoaderTests
{
    private static HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        return new HttpClient(handler.Object);
    }

    [Fact]
    public async Task LoadAsync_ExtractsVisibleText()
    {
        var html = "<html><body><p>Hello World</p></body></html>";
        var client = CreateMockHttpClient(html);
        var loader = new WebPageLoader(client);

        var doc = await loader.LoadAsync("https://example.com");

        doc.PageContent.Should().Contain("Hello World");
    }

    [Fact]
    public async Task LoadAsync_SetsSourceToUrl()
    {
        var html = "<html><body><p>Content</p></body></html>";
        var client = CreateMockHttpClient(html);
        var loader = new WebPageLoader(client);

        var doc = await loader.LoadAsync("https://example.com/page");

        doc.Metadata["source"].Should().Be("https://example.com/page");
    }

    [Fact]
    public async Task LoadAsync_StripsScriptTags()
    {
        var html = "<html><body><script>var x = 1;</script><p>Visible text</p></body></html>";
        var client = CreateMockHttpClient(html);
        var loader = new WebPageLoader(client);

        var doc = await loader.LoadAsync("https://example.com");

        doc.PageContent.Should().Contain("Visible text");
        doc.PageContent.Should().NotContain("var x = 1");
    }

    [Fact]
    public async Task LoadAsync_StripsStyleTags()
    {
        var html = "<html><head><style>body { color: red; }</style></head><body><p>Content</p></body></html>";
        var client = CreateMockHttpClient(html);
        var loader = new WebPageLoader(client);

        var doc = await loader.LoadAsync("https://example.com");

        doc.PageContent.Should().Contain("Content");
        doc.PageContent.Should().NotContain("color: red");
    }

    [Fact]
    public async Task LoadAsync_StripsNavHeaderFooter()
    {
        var html = @"<html><body>
            <nav>Navigation links</nav>
            <header>Site header</header>
            <main><p>Main content here</p></main>
            <footer>Footer info</footer>
        </body></html>";
        var client = CreateMockHttpClient(html);
        var loader = new WebPageLoader(client);

        var doc = await loader.LoadAsync("https://example.com");

        doc.PageContent.Should().Contain("Main content here");
        doc.PageContent.Should().NotContain("Navigation links");
        doc.PageContent.Should().NotContain("Site header");
        doc.PageContent.Should().NotContain("Footer info");
    }

    [Fact]
    public async Task LoadAsync_ThrowsOnEmptyUrl()
    {
        var client = CreateMockHttpClient("");
        var loader = new WebPageLoader(client);

        var act = () => loader.LoadAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void ExtractVisibleText_CollapsesWhitespace()
    {
        var html = "<html><body><p>Hello</p>   <p>World</p></body></html>";

        var text = WebPageLoader.ExtractVisibleText(html);

        // Should not have excessive whitespace
        text.Should().NotContainAny(new[] { "  " });
    }

    [Fact]
    public void ExtractVisibleText_DecodesHtmlEntities()
    {
        var html = "<html><body><p>Hello &amp; World &lt;3</p></body></html>";

        var text = WebPageLoader.ExtractVisibleText(html);

        text.Should().Contain("Hello & World <3");
    }

    [Fact]
    public void ExtractVisibleText_EmptyHtml_ReturnsEmpty()
    {
        var text = WebPageLoader.ExtractVisibleText("");

        text.Should().BeEmpty();
    }
}
