using FluentAssertions;
using RagChatbot.Core.Utilities;

namespace RagChatbot.Tests.Utilities;

public class ProjectNameNormalizerTests
{
    [Fact]
    public void Normalize_LowercaseInput_ReturnsUppercase()
    {
        ProjectNameNormalizer.Normalize("nesa").Should().Be("NESA");
    }

    [Fact]
    public void Normalize_SpacesInInput_ReplacesWithDash()
    {
        ProjectNameNormalizer.Normalize("My Project").Should().Be("MY-PROJECT");
    }

    [Fact]
    public void Normalize_SpaceDashSpace_CollapsesToSingleDash()
    {
        ProjectNameNormalizer.Normalize("Project - A").Should().Be("PROJECT-A");
    }

    [Fact]
    public void Normalize_MultipleDashes_CollapsesToSingle()
    {
        ProjectNameNormalizer.Normalize("A--B").Should().Be("A-B");
    }

    [Fact]
    public void Normalize_LeadingTrailingWhitespace_Trimmed()
    {
        ProjectNameNormalizer.Normalize("  test  ").Should().Be("TEST");
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        ProjectNameNormalizer.Normalize(null!).Should().Be("");
    }

    [Fact]
    public void Normalize_EmptyInput_ReturnsEmpty()
    {
        ProjectNameNormalizer.Normalize("").Should().Be("");
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsEmpty()
    {
        ProjectNameNormalizer.Normalize("   ").Should().Be("");
    }

    [Fact]
    public void Normalize_ComplexInput_AllRulesApplied()
    {
        ProjectNameNormalizer.Normalize("a - - b").Should().Be("A-B");
    }
}
