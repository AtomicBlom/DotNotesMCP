using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That writing a note's metadata changes the keys named and nothing else.
/// <para>
/// This is the code that can damage something a person wrote. The store is a vault they edit in
/// Obsidian, with their own properties, their own comments and their own quoting, and a writer that
/// rebuilt the document would reformat all of it on every save -- turning a re-index of a corpus
/// nobody touched into a diff on every file.
/// </para>
/// </summary>
public sealed class FrontmatterSpliceTests
{
	[Test]
	public void A_replaced_key_keeps_its_position()
	{
		const string Text = "---\nname: one\ndn-gist: old\ntags: [a]\n---\nBody.\n";

		var spliced = FrontmatterSplice.Apply(Text, new Dictionary<string, string?>
		{
			["dn-gist"] = "dn-gist: new",
		});

		spliced.ShouldBe("---\nname: one\ndn-gist: new\ntags: [a]\n---\nBody.\n");
	}

	/// <summary>
	/// Obsidian shows properties in file order. A machine key that shuffled to the top on every write
	/// would make each re-index a visible change to a file nobody edited.
	/// </summary>
	[Test]
	public void A_new_key_is_appended_and_the_rest_keeps_its_order()
	{
		const string Text = "---\nname: one\ntags: [a]\n---\nBody.\n";

		var spliced = FrontmatterSplice.Apply(Text, new Dictionary<string, string?>
		{
			["dn-gist"] = "dn-gist: a summary",
		});

		spliced.ShouldBe("---\nname: one\ntags: [a]\ndn-gist: a summary\n---\nBody.\n");
	}

	[Test]
	public void A_null_replacement_removes_the_key()
	{
		const string Text = "---\nname: one\ndn-gist: old\ntags: [a]\n---\nBody.\n";

		var spliced = FrontmatterSplice.Apply(Text, new Dictionary<string, string?> { ["dn-gist"] = null });

		spliced.ShouldBe("---\nname: one\ntags: [a]\n---\nBody.\n");
	}

	/// <summary>A block sequence is several lines under one key, and all of them belong to it.</summary>
	[Test]
	public void A_block_sequence_is_replaced_whole()
	{
		const string Text = "---\nname: one\ndn-asks:\n  - old one?\n  - old two?\ntags: [a]\n---\nBody.\n";

		var spliced = FrontmatterSplice.Apply(Text, new Dictionary<string, string?>
		{
			["dn-asks"] = FrontmatterSplice.Entry("dn-asks", ["new?"], "\n"),
		});

		spliced.ShouldBe("---\nname: one\ndn-asks:\n  - new?\ntags: [a]\n---\nBody.\n");
	}

	/// <summary>
	/// The point of splicing rather than rebuilding: a person's comments, blank lines, quoting and
	/// property order all come out exactly as they went in.
	/// </summary>
	[Test]
	public void Everything_not_named_survives_byte_for_byte()
	{
		const string Text = """
			---
			# my own notes about this note
			name: 'one'
			description: >
			  a folded
			  description

			tags:   [ a,  b ]
			dn-gist: old
			---
			Body.

			""";

		var spliced = FrontmatterSplice.Apply(
			Text.Replace("\r\n", "\n", StringComparison.Ordinal),
			new Dictionary<string, string?> { ["dn-gist"] = "dn-gist: new" });

		spliced.ShouldContain("# my own notes about this note");
		spliced.ShouldContain("name: 'one'");
		spliced.ShouldContain("description: >\n  a folded\n  description");
		spliced.ShouldContain("tags:   [ a,  b ]");
		spliced.ShouldContain("dn-gist: new");
		spliced.ShouldNotContain("dn-gist: old");
	}

	[Test]
	public void A_note_with_no_block_gets_one()
	{
		var spliced = FrontmatterSplice.Apply("Body.\n", new Dictionary<string, string?>
		{
			["name"] = "name: one",
		});

		spliced.ShouldBe("---\nname: one\n---\nBody.\n");
	}

	/// <summary>The file's own line ending is what the new lines use, not the platform's.</summary>
	[Test]
	public void The_files_line_ending_is_preserved()
	{
		const string Text = "---\r\nname: one\r\n---\r\nBody.\r\n";

		var spliced = FrontmatterSplice.Apply(Text, new Dictionary<string, string?>
		{
			["dn-gist"] = "dn-gist: new",
		});

		spliced.ShouldBe("---\r\nname: one\r\ndn-gist: new\r\n---\r\nBody.\r\n");
		spliced.ShouldNotContain("\n\n");
	}

	/// <summary>Splicing nothing is not a rewrite, so a no-op cannot cost a sync.</summary>
	[Test]
	public void Splicing_nothing_returns_the_same_text()
	{
		const string Text = "---\nname: one\n---\nBody.\n";

		FrontmatterSplice.Apply(Text, new Dictionary<string, string?>()).ShouldBe(Text);
	}

	/// <summary>What is spliced in must read back as what was meant to go in.</summary>
	[Test]
	public void A_spliced_value_round_trips_through_the_parser()
	{
		const string Awkward = "no: really: it has colons, and a #hash";

		var spliced = FrontmatterSplice.Apply(
			"---\nname: one\n---\nBody.\n",
			new Dictionary<string, string?> { ["dn-gist"] = FrontmatterSplice.Entry("dn-gist", Awkward) });

		var parsed = NoteFrontmatter.Parse(FrontmatterBlock.Split(spliced).Yaml);

		parsed.Error.ShouldBeNull();
		parsed.Scalar("dn-gist").ShouldBe(Awkward);
		parsed.Scalar("name").ShouldBe("one");
	}

	/// <summary>
	/// A value YAML would otherwise read as something else keeps its type. A description reading
	/// "no" coming back as false is the kind of wrong nothing downstream notices.
	/// </summary>
	[Test]
	[Arguments("no")]
	[Arguments("yes")]
	[Arguments("true")]
	[Arguments("null")]
	[Arguments("3.14")]
	[Arguments("2026-09-19")]
	public void A_value_that_looks_like_another_type_stays_text(string value)
	{
		var spliced = FrontmatterSplice.Apply(
			"---\nname: one\n---\n",
			new Dictionary<string, string?> { ["dn-gist"] = FrontmatterSplice.Entry("dn-gist", value) });

		NoteFrontmatter.Parse(FrontmatterBlock.Split(spliced).Yaml).Scalar("dn-gist").ShouldBe(value);
	}
}
