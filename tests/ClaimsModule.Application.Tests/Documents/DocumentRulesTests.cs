using ClaimsModule.Application.Documents;

namespace ClaimsModule.Application.Tests.Documents;

/// <summary>Task 29: the S13 upload rules (allowlist, sanitiser, D-29 storage path).</summary>
public sealed class DocumentRulesTests
{
    [Theory]
    [InlineData("police-report.pdf", "application/pdf")]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("scan.jpeg", "image/jpeg")]
    [InlineData("damage.png", "image/png")]
    [InlineData("estimate.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("ledger.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("notes.txt", "text/plain; charset=utf-8")]
    [InlineData("export.csv", "application/vnd.ms-excel")]
    public void Allowed_types_pass(string fileName, string contentType) =>
        DocumentRules.IsAllowed(fileName, contentType).ShouldBeTrue();

    [Theory]
    [InlineData("tool.exe", "application/octet-stream")]
    [InlineData("page.html", "text/html")]
    [InlineData("photo.png", "application/pdf")] // extension and declared type must match
    [InlineData("report.pdf.exe", "application/pdf")]
    [InlineData("no-extension", "application/pdf")]
    public void Other_types_are_refused(string fileName, string contentType) =>
        DocumentRules.IsAllowed(fileName, contentType).ShouldBeFalse();

    [Theory]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\Users\\jane\\Desktop\\claim form.pdf", "claim form.pdf")]
    [InlineData("..\\..\\secret.pdf", "secret.pdf")]
    [InlineData("  .hidden.pdf ", "hidden.pdf")]
    [InlineData("what?<is>|this*.pdf", "what__is__this_.pdf")]
    [InlineData("tab\tand\nnewline.txt", "tab_and_newline.txt")]
    [InlineData("many    spaces.pdf", "many spaces.pdf")]
    [InlineData("", "document")]
    [InlineData("...", "document")]
    [InlineData(null, "document")]
    public void File_names_are_sanitised(string? input, string expected) =>
        DocumentRules.SanitiseFileName(input).ShouldBe(expected);

    [Fact]
    public void Long_names_are_cut_to_200_characters_keeping_the_extension()
    {
        var name = DocumentRules.SanitiseFileName(new string('a', 300) + ".pdf");

        name.Length.ShouldBe(DocumentRules.MaxFileNameLength);
        name.ShouldEndWith(".pdf");
    }

    [Fact]
    public void Storage_path_is_organisation_claim_and_unique_document_prefix()
    {
        var org = Guid.Parse("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64");
        var claim = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var doc = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        DocumentRules.StoragePath(org, claim, doc, "report.pdf")
            .ShouldBe("3f2b8c1e-6d4a-4e9b-a7c2-5d1e0f9b8a64/11111111-2222-3333-4444-555555555555/aaaaaaaabbbbccccddddeeeeeeeeeeee-report.pdf");
    }
}
