namespace Broadside.Tools.ReleaseNotes.Tests;

/// <summary>Commits since the last tag are grouped by Conventional Commit type, then by scope.</summary>
public sealed class GroupingTests
{
    private static string Notes(TempRepository repo, ReleaseNotesOptions? options = null) =>
        ReleaseNotesGenerator.Generate(repo.Root, options ?? new ReleaseNotesOptions()).Markdown;

    [Fact]
    public void A_feature_since_the_last_tag_is_listed_under_features_with_its_scope()
    {
        using var repo = new TempRepository();
        repo.Commit("feat(fonts): parse CFF (#1)").Tag("v0.1.0-preview.1")
            .Commit("feat(cos): read object streams (#2)");

        string notes = Notes(repo);

        Assert.Contains("### Features", notes, StringComparison.Ordinal);
        Assert.Contains("- **cos:** read object streams ([#2](https://github.com/michaelstonis/Broadside/issues/2))", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("parse CFF", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_tag_is_the_highest_version_not_the_last_in_text_order()
    {
        using var repo = new TempRepository();
        repo.Commit("fix: one").Tag("v0.1.0-preview.9")
            .Commit("fix: two").Tag("v0.1.0-preview.10")
            .Commit("fix: three");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        Assert.Equal("v0.1.0-preview.10", result.From);
        Assert.Equal(["three"], result.Entries.Select(e => e.Description));
        Assert.StartsWith("## Unreleased (", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_branch_that_never_merged_the_release_commit_is_compared_with_the_latest_release()
    {
        // Releases are tagged on main's merge commit, which the integration branch never merges back.
        using var repo = new TempRepository();
        repo.Tag("v0.1.0-preview.1")
            .Checkout("spec", create: true)
            .Commit("feat: one")
            .Checkout("main")
            .Merge("spec", "Merge pull request #2 from owner/release/0.1.0-preview.2\n\nBroadside v0.1.0-preview.2")
            .Tag("v0.1.0-preview.2")
            .Checkout("spec")
            .Commit("feat: two");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        Assert.Equal("v0.1.0-preview.2", result.From);
        Assert.Equal(["two"], result.Entries.Select(e => e.Description));
    }

    [Fact]
    public void A_tagged_head_is_released_against_the_previous_tag_under_its_own_version()
    {
        using var repo = new TempRepository();
        repo.Commit("fix: one").Tag("v1.0.0-rc.1")
            .Commit("fix: two").Tag("v1.0.0");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        Assert.Equal("v1.0.0-rc.1", result.From);
        Assert.Equal("1.0.0", result.Version);
        Assert.StartsWith("## 1.0.0 (", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("compare/v1.0.0-rc.1...v1.0.0", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Sections_follow_a_fixed_order_and_scopes_are_sorted_within_a_section()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0")
            .Commit("perf(rendering): faster blends")
            .Commit("fix(fonts): wrong widths")
            .Commit("feat(text): extract words")
            .Commit("feat(cos): repair xref")
            .Commit("ci: cache packages")
            .Commit("bench: add a benchmark")
            .Commit("docs: guide");

        string notes = Notes(repo);

        string[] order = ["### Features", "**cos:** repair xref", "**text:** extract words", "### Fixes", "### Performance", "### Documentation", "### Benchmarks", "### Build and CI"];
        int last = -1;
        foreach (string text in order)
        {
            int index = notes.IndexOf(text, StringComparison.Ordinal);
            Assert.True(index > last, $"'{text}' is out of order in:\n{notes}");
            last = index;
        }
    }

    [Fact]
    public void A_title_that_is_not_conventional_is_listed_under_other_changes_and_release_commits_are_left_out()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0")
            .Commit("Update README")
            .Commit("chore(release): changelog for 0.2.0");

        string notes = Notes(repo);

        Assert.Contains("### Other changes\n\n- Update README\n", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("changelog for 0.2.0", notes, StringComparison.Ordinal);
    }
}
