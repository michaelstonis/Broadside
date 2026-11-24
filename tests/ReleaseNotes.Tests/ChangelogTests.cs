namespace Broadside.Tools.ReleaseNotes.Tests;

/// <summary>CHANGELOG.md accumulates the notes: newest release first, a release written twice is replaced, not repeated.</summary>
public sealed class ChangelogTests
{
    private const string Header = "# Changelog\n\nIntro.\n\n";

    [Fact]
    public void A_new_release_goes_above_the_older_ones()
    {
        string existing = Header + "## 0.1.0 (2026-01-01)\n\nOld.\n";

        string updated = Changelog.Update(existing, "## 0.2.0 (2026-02-01)\n\nNew.\n");

        Assert.Equal(Header + "## 0.2.0 (2026-02-01)\n\nNew.\n\n## 0.1.0 (2026-01-01)\n\nOld.\n", updated);
    }

    [Fact]
    public void A_release_written_again_replaces_its_section()
    {
        string existing = Header + "## 0.2.0 (2026-02-01)\n\nDraft.\n\n## 0.1.0 (2026-01-01)\n\nOld.\n";

        string updated = Changelog.Update(existing, "## 0.2.0 (2026-02-02)\n\nFinal.\n");

        Assert.Equal(Header + "## 0.2.0 (2026-02-02)\n\nFinal.\n\n## 0.1.0 (2026-01-01)\n\nOld.\n", updated);
    }

    [Fact]
    public void Releasing_replaces_the_unreleased_section()
    {
        string existing = Header + "## Unreleased (2026-02-01)\n\nPending.\n\n## 0.1.0 (2026-01-01)\n\nOld.\n";

        string updated = Changelog.Update(existing, "## 0.2.0 (2026-02-02)\n\nFinal.\n");

        Assert.Equal(Header + "## 0.2.0 (2026-02-02)\n\nFinal.\n\n## 0.1.0 (2026-01-01)\n\nOld.\n", updated);
    }

    [Fact]
    public void An_empty_changelog_gets_a_header()
    {
        string updated = Changelog.Update(string.Empty, "## 0.1.0 (2026-01-01)\n\nFirst.\n");

        Assert.StartsWith("# Changelog\n", updated, StringComparison.Ordinal);
        Assert.EndsWith("## 0.1.0 (2026-01-01)\n\nFirst.\n", updated, StringComparison.Ordinal);
    }
}
