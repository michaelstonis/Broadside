namespace Broadside.Tools.ReleaseNotes.Tests;

/// <summary>
/// Merged pull requests: GitHub's merge commit carries the pull request title in its body; a conventional one is one entry, any
/// other merge (<c>merge: #N ...</c>, a release pull request) is replaced by the commits it brought in.
/// </summary>
public sealed class MergeTests
{
    [Fact]
    public void A_merged_pull_request_with_a_conventional_title_is_one_entry_linked_to_the_pull_request()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0")
            .Checkout("602-blend", create: true)
            .Commit("perf(rendering): step one")
            .Commit("perf(rendering): step two")
            .Checkout("main")
            .Merge("602-blend", "Merge pull request #613 from owner/602-blend\n\nperf(rendering): blend a run of equal pixels once (#602)");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        ReleaseNoteEntry entry = Assert.Single(result.Entries);
        Assert.Equal("blend a run of equal pixels once (#602) (#613)", entry.Description);
        Assert.Contains("[#613](https://github.com/michaelstonis/Broadside/issues/613)", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_without_a_conventional_title_lists_the_commits_of_its_branch()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0")
            .Checkout("428-skia", create: true)
            .Commit("feat(skia): decode images at the drawn size")
            .Commit("test(skia): cover scaled decode")
            .Checkout("main")
            .Merge("428-skia", "merge: #428 part 1/5 Skia: decode images at the resolution they are drawn (PR #429)");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        Assert.Equal(["decode images at the drawn size", "cover scaled decode"], result.Entries.Select(e => e.Description));
        Assert.DoesNotContain("part 1/5", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_pull_request_is_replaced_by_the_pull_requests_merged_into_its_branch()
    {
        using var repo = new TempRepository();
        repo.Tag("v0.1.0-preview.1")
            .Checkout("spec", create: true)
            .Checkout("10-fonts", create: true)
            .Commit("feat(fonts): parse CFF")
            .Checkout("spec")
            .Merge("10-fonts", "Merge pull request #11 from owner/10-fonts\n\nfeat(fonts): parse CFF charstrings (#10)")
            .Commit("fix(cos): recover startxref (#12)")
            .Checkout("main")
            .Commit("docs: fix a typo (#13)")
            .Checkout("spec")
            .Merge("main", "Merge main into spec")
            .Checkout("main")
            .Merge("spec", "Merge pull request #20 from owner/release/0.1.0-preview.2\n\nBroadside v0.1.0-preview.2: fonts and repairs")
            .Tag("v0.1.0-preview.2");

        ReleaseNotesResult result = ReleaseNotesGenerator.Generate(repo.Root, new ReleaseNotesOptions());

        Assert.Equal("v0.1.0-preview.1", result.From);
        Assert.Equal(
            ["docs: fix a typo (#13)", "feat: parse CFF charstrings (#10) (#11)", "fix: recover startxref (#12)"],
            result.Entries.Select(e => $"{e.Type}: {e.Description}").Order(StringComparer.Ordinal));
    }
}
