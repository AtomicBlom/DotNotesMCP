using DotNotes.Contracts;
using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That the index cannot disagree with the notes beside it, and that regenerating it is usually not
/// a change at all.
/// </summary>
public sealed class NoteIndexFileTests
{
	private static NoteHeading Heading(string name, string description = "", string? gist = null) => new()
	{
		Name = name,
		Description = description,
		Scope = NoteScope.Machine,
		Type = NoteType.Project,
		Path = $"{name}.md",
		Revision = "0",
		Gist = gist,
	};

	[Test]
	public void Every_note_gets_a_line_that_links_to_it()
	{
		var index = NoteIndexFile.Render("rosemcp", [Heading("tray-dev-loop", "How to run a dev tray")]);

		index.ShouldContain("- [[tray-dev-loop]] — How to run a dev tray");
	}

	/// <summary>
	/// The gist is written to be read in a list, which is what this is, so it wins over the
	/// description where the indexing mode has run.
	/// </summary>
	[Test]
	public void The_gist_is_preferred_to_the_description()
	{
		var index = NoteIndexFile.Render(
			"rosemcp",
			[Heading("one", "the author's line", gist: "the indexer's line")]);

		index.ShouldContain("the indexer's line");
		index.ShouldNotContain("the author's line");
	}

	/// <summary>
	/// An index whose order wandered would be rewritten whenever an unrelated note changed, and on a
	/// synced store every rewrite is a replication and a stored revision.
	/// </summary>
	[Test]
	public void The_same_notes_always_produce_the_same_bytes()
	{
		NoteHeading[] one = [Heading("b"), Heading("a"), Heading("c")];
		NoteHeading[] other = [Heading("c"), Heading("a"), Heading("b")];

		NoteIndexFile.Render("r", other).ShouldBe(NoteIndexFile.Render("r", one));
	}

	[Test]
	public void Notes_are_listed_by_name()
	{
		var index = NoteIndexFile.Render("r", [Heading("zeta"), Heading("alpha")]);

		index.IndexOf("alpha", StringComparison.Ordinal)
			.ShouldBeLessThan(index.IndexOf("zeta", StringComparison.Ordinal));
	}

	/// <summary>
	/// An index of everything would otherwise match every search, and a person opening it should see
	/// at once that editing it is pointless.
	/// </summary>
	[Test]
	public void A_generated_index_says_so_and_is_recognisable()
	{
		var index = NoteIndexFile.Render("r", [Heading("one")]);

		index.ShouldContain("dotnotes-generated: true");
		NoteIndexFile.IsGenerated(index).ShouldBeTrue();
		NoteIndexFile.IsGenerated("---\nname: one\n---\nAn ordinary note.\n").ShouldBeFalse();
	}

	/// <summary>An empty folder says so, rather than being a file with a heading and nothing under it.</summary>
	[Test]
	public void An_empty_folder_says_it_is_empty() =>
		NoteIndexFile.Render("r", []).ShouldContain("No notes yet.");

	/// <summary>
	/// A cross-store link cannot resolve in Obsidian, so the committed notes are listed here. A
	/// listing rather than a mirror: a copy would be a second thing to keep true.
	/// </summary>
	[Test]
	public void The_other_store_is_listed_rather_than_copied()
	{
		var index = NoteIndexFile.Render("rosemcp", [Heading("private-one")], "\n", [Heading("committed-one")]);

		index.ShouldContain("## In the repository");
		index.ShouldContain("committed-one");
	}

	/// <summary>Regenerating must not rewrite every line of a file that uses the other ending.</summary>
	[Test]
	public void The_existing_line_ending_is_kept()
	{
		var index = NoteIndexFile.Render("r", [Heading("one")], "\r\n");

		index.ShouldContain("\r\n");
		index.Replace("\r\n", string.Empty, StringComparison.Ordinal).ShouldNotContain("\n");
	}

	/// <summary>A note pinned to one machine says so, because the vault may be read from the other.</summary>
	[Test]
	public void A_machine_specific_note_is_marked()
	{
		var heading = Heading("arm64-quirk", "the quirk") with { Machines = ["surface-arm"] };

		NoteIndexFile.Render("r", [heading]).ShouldContain("_(surface-arm)_");
	}
}
