using DotNotes.Contracts;
using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That a note survives being read however it was written.
/// <para>
/// A store is a folder a person edits by hand, so every field has to survive not being there. A
/// note that cannot be read is a note that cannot be found, which is worse than one read
/// imperfectly -- what is wrong is reported by <c>note_check</c> rather than by refusing to answer.
/// </para>
/// </summary>
public sealed class NoteReaderTests
{
	private static Note Parse(string content) =>
		NoteReader.Parse(Path.Combine("store", "arm64-msbuild-locator.md"), NoteScope.Machine, content);

	[Test]
	public void A_complete_note_reads_every_field()
	{
		var note = Parse("""
			---
			name: arm64-msbuild-locator
			description: MSBuildLocator picks the x64 SDK on ARM64
			type: project
			tags: [arm64, msbuild]
			machines: [surface-arm]
			created: 2026-09-19
			updated: 2026-09-20
			dn-gist: It resolves to the x64 SDK unless the architecture is pinned first.
			---
			Body, with a [[link]].

			""");

		note.Heading.Name.ShouldBe("arm64-msbuild-locator");
		note.Heading.Description.ShouldBe("MSBuildLocator picks the x64 SDK on ARM64");
		note.Heading.Type.ShouldBe(NoteType.Project);
		note.Heading.Tags.ShouldBe(["arm64", "msbuild"]);
		note.Heading.Machines.ShouldBe(["surface-arm"]);
		note.Heading.Created.ShouldBe(new DateOnly(2026, 9, 19));
		note.Heading.Updated.ShouldBe(new DateOnly(2026, 9, 20));
		note.Heading.Enriched.ShouldBeTrue();
		note.Heading.Gist.ShouldNotBeNull();
		note.Links.ShouldHaveSingleItem().Target.ShouldBe("link");
	}

	/// <summary>A note dropped into the vault by hand is still a note.</summary>
	[Test]
	public void A_note_with_no_frontmatter_is_named_after_its_file()
	{
		var note = Parse("Just some prose about MSBuildLocator.\n");

		note.Heading.Name.ShouldBe("arm64-msbuild-locator");
		note.Heading.Type.ShouldBe(NoteType.Project);
		note.Heading.Enriched.ShouldBeFalse();
	}

	/// <summary>An empty description in a listing reads as a note with nothing in it.</summary>
	[Test]
	public void A_missing_description_falls_back_to_the_first_line_of_prose()
	{
		var note = Parse("---\nname: one\n---\n\n# A heading\n\nThe first real sentence.\n");

		note.Heading.Description.ShouldBe("A heading");
	}

	/// <summary>
	/// A value a person typed is not a tool argument. The cost of an unrecognised type is a facet,
	/// not a lost note, so it defaults where an argument would refuse.
	/// </summary>
	[Test]
	[Arguments("user", NoteType.User)]
	[Arguments("FEEDBACK", NoteType.Feedback)]
	[Arguments("reference", NoteType.Reference)]
	[Arguments("nonsense", NoteType.Project)]
	[Arguments(null, NoteType.Project)]
	public void An_unrecognised_type_is_a_project_note(string? declared, NoteType expected)
	{
		var yaml = declared is null ? "name: one" : $"name: one\ntype: {declared}";

		Parse($"---\n{yaml}\n---\nBody.\n").Heading.Type.ShouldBe(expected);
	}

	[Test]
	public void A_malformed_date_is_no_date() =>
		Parse("---\nname: one\nupdated: someday\n---\nBody.\n").Heading.Updated.ShouldBeNull();

	/// <summary>A note whose metadata a person broke still has prose worth finding.</summary>
	[Test]
	public void A_broken_block_still_yields_a_readable_note()
	{
		var note = Parse("---\nname: [unclosed\n  : :\n---\nThe prose survives.\n");

		note.Frontmatter.Error.ShouldNotBeNull();
		note.Body.ShouldContain("The prose survives.");
		note.Heading.Name.ShouldBe("arm64-msbuild-locator");
	}

	/// <summary>
	/// The dialect Claude Code's own memory writes, which is what a note copied into a store by hand
	/// looks like. Its type has to survive: silently becoming a project note is a facet quietly lost
	/// across a whole corpus, with nothing to notice it.
	/// </summary>
	[Test]
	public void The_nested_dialect_keeps_its_type()
	{
		var note = Parse("""
			---
			name: tray-dev-loop
			description: How to run a development tray
			metadata:
			  node_type: memory
			  type: feedback
			---
			Body.
			""");

		note.Heading.Type.ShouldBe(NoteType.Feedback);
		note.Heading.Description.ShouldBe("How to run a development tray");
	}

	[Test]
	public void A_name_that_is_not_a_slug_becomes_one() =>
		Parse("---\nname: Tray Dev Loop\n---\nBody.\n").Heading.Name.ShouldBe("tray-dev-loop");
}
