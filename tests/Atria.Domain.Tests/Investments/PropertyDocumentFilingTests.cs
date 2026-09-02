using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Filing of the paperwork an owner attaches: what each document is, and what to call it.
/// <para>
/// The request document asks for a technical passport, photo materials and a construction schedule
/// at once. Filed as one undifferentiated pile of file names, the lawyer who needs the cadastre
/// extract has to find it by eye — so a document carries its category, and a name a person can read.
/// </para>
/// </summary>
public sealed class PropertyDocumentFilingTests
{
    private static Property NewIssue() => Property.Create(
        "ЖК на Токомбаева", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void ADocumentIsUnfiledUnlessItSaysWhatItIs()
    {
        // What every document uploaded before the category existed is.
        var issue = NewIssue();

        var doc = issue.AddDocument("/media/documents/a.pdf", "a.pdf", "application/pdf");

        doc.Category.Should().Be(PropertyDocumentCategory.Unspecified);
        doc.Title.Should().BeNull();
    }

    [Fact]
    public void ADocumentKeepsItsCategoryAndTitle()
    {
        var issue = NewIssue();

        var doc = issue.AddDocument(
            "/media/documents/scan_0012_final.pdf", "scan_0012_final.pdf", "application/pdf",
            PropertyDocumentCategory.TechnicalPassport, "Технический паспорт");

        doc.Category.Should().Be(PropertyDocumentCategory.TechnicalPassport);
        doc.Title.Should().Be("Технический паспорт");
        // The scanner's name for the file is not lost — both are facts about the same document.
        doc.FileName.Should().Be("scan_0012_final.pdf");
    }

    [Fact]
    public void DisplayNameFallsBackToTheFileNameWhenNobodyNamedIt()
    {
        var issue = NewIssue();

        var doc = issue.AddDocument("/media/documents/a.pdf", "выписка.pdf", "application/pdf");

        doc.DisplayName.Should().Be("выписка.pdf");
    }

    [Fact]
    public void DisplayNamePrefersTheTitleAPersonGaveIt()
    {
        var issue = NewIssue();

        var doc = issue.AddDocument(
            "/media/documents/a.pdf", "scan_0012_final(2).pdf", "application/pdf",
            PropertyDocumentCategory.Legal, "Выписка из Кадастра");

        doc.DisplayName.Should().Be("Выписка из Кадастра");
    }

    [Fact]
    public void ABlankTitleIsNoTitleAtAll()
    {
        var issue = NewIssue();

        var doc = issue.AddDocument(
            "/media/documents/a.pdf", "a.pdf", "application/pdf",
            PropertyDocumentCategory.Layout, "   ");

        doc.Title.Should().BeNull();
        doc.DisplayName.Should().Be("a.pdf");
    }

    [Fact]
    public void ATitleTooLongForTheColumnIsRefused()
    {
        var issue = NewIssue();

        var act = () => issue.AddDocument(
            "/media/documents/a.pdf", "a.pdf", "application/pdf",
            PropertyDocumentCategory.Legal, new string('я', PropertyDocument.MaxTitle + 1));

        act.Should().Throw<DomainException>();
    }
}
