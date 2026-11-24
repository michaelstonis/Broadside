namespace Broadside.Tools.ReleaseNotes.Tests;

/// <summary>Breaking changes (<c>!</c> after the type, a <c>BREAKING CHANGE:</c> footer, removed public API) are called out first.</summary>
public sealed class BreakingChangeTests
{
    [Fact]
    public void A_bang_after_the_type_is_a_breaking_change()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0").Commit("feat(cos)!: rename CosName.Value to CosName.Text");

        string notes = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions()).Markdown;

        Assert.StartsWith("### Breaking changes\n\n- **cos:** rename CosName.Value to CosName.Text\n", notes[notes.IndexOf("### ", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void A_breaking_change_footer_gives_the_breaking_note()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0").Commit("refactor(text): split extraction options\n\nBREAKING CHANGE: TextOptions.Layout moved to TextLayoutOptions.");

        ReleaseNoteEntry entry = Assert.Single(ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions()).Entries);

        Assert.Equal("TextOptions.Layout moved to TextLayoutOptions.", entry.BreakingNote);
    }

    [Fact]
    public void A_breaking_commit_inside_a_merged_pull_request_makes_the_pull_request_breaking()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0")
            .Checkout("50-api", create: true)
            .Commit("refactor(api)!: drop PdfDocument.Load")
            .Checkout("main")
            .Merge("50-api", "Merge pull request #51 from owner/50-api\n\nrefactor(api): tidy the document API (#50)");

        string notes = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions()).Markdown;

        Assert.Contains("### Breaking changes\n\n- **api:** drop PdfDocument.Load\n", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_api_entries_added_and_removed_since_the_last_tag_are_listed_per_package()
    {
        using var repo = new TempRepository();
        repo.CommitFile("src/Broadside/PublicAPI.Shipped.txt", "#nullable enable\nBroadside.PdfDocument\nBroadside.PdfDocument.Load() -> void\n", "build: ship the API")
            .CommitFile("src/Broadside/PublicAPI.Unshipped.txt", "#nullable enable\n", "build: start unshipped")
            .Tag("v0.1.0")
            .CommitFile("src/Broadside/PublicAPI.Unshipped.txt", "#nullable enable\n*REMOVED*Broadside.PdfDocument.Load() -> void\nBroadside.PdfDocument.Open() -> void\n", "feat(api): open instead of load");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        PackageApiChange change = Assert.Single(result.ApiChanges);
        Assert.Equal("Broadside", change.Package);
        Assert.Equal(["Broadside.PdfDocument.Open() -> void"], change.Added);
        Assert.Equal(["Broadside.PdfDocument.Load() -> void"], change.Removed);
        Assert.Contains("| `Broadside` | 1 | 1 |", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("- Public API removed or changed in `Broadside`", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("([diff](https://github.com/michaelstonis/Broadside/compare/v0.1.0...", result.Markdown, StringComparison.Ordinal);
    }
}
