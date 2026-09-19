using DotNotes.Notes.Files;

namespace DotNotes.UnitTests;

/// <summary>
/// That a note reaches disk whole, and that writing one that has not changed is not a write.
/// </summary>
public sealed class NoteFileTests
{
	[Test]
	public void A_note_round_trips()
	{
		using var fixture = GitFixture.Create();
		var path = Path.Combine(fixture.Plain("vault"), "one.md");

		NoteFile.Write(path, "---\nname: one\n---\nBody.\n").ShouldBeTrue();

		NoteFile.Read(path).ShouldBe("---\nname: one\n---\nBody.\n");
	}

	/// <summary>
	/// On a synced store every write is a replication and a stored revision, so re-indexing a corpus
	/// that has not changed must cost nothing at all.
	/// </summary>
	[Test]
	public void Writing_the_same_content_is_not_a_write()
	{
		using var fixture = GitFixture.Create();
		var path = Path.Combine(fixture.Plain("vault"), "one.md");

		NoteFile.Write(path, "same").ShouldBeTrue();

		var written = File.GetLastWriteTimeUtc(path);

		NoteFile.Write(path, "same").ShouldBeFalse();

		File.GetLastWriteTimeUtc(path).ShouldBe(written);
	}

	/// <summary>Nothing is left behind for a sync service to replicate or a person to wonder about.</summary>
	[Test]
	public void A_write_leaves_no_temporary_file()
	{
		using var fixture = GitFixture.Create();
		var vault = fixture.Plain("vault");

		NoteFile.Write(Path.Combine(vault, "one.md"), "content");

		Directory.GetFiles(vault).ShouldBe([Path.Combine(vault, "one.md")]);
	}

	[Test]
	public void Reading_a_missing_note_is_null_rather_than_an_error()
	{
		using var fixture = GitFixture.Create();

		NoteFile.Read(Path.Combine(fixture.Plain("vault"), "absent.md")).ShouldBeNull();
	}

	[Test]
	public void Deleting_reports_whether_there_was_a_note()
	{
		using var fixture = GitFixture.Create();
		var path = Path.Combine(fixture.Plain("vault"), "one.md");

		NoteFile.Delete(path).ShouldBeFalse();
		NoteFile.Write(path, "content");
		NoteFile.Delete(path).ShouldBeTrue();
	}

	/// <summary>
	/// The same note is CRLF in a checkout under this repository's eol attribute and LF wherever git
	/// stored it. Hashing the bytes would make a revision depend on which machine checked it out, and
	/// every clone would see every note as changed.
	/// </summary>
	[Test]
	public void A_revision_does_not_depend_on_line_endings() =>
		NoteFile.Revision("---\r\nname: one\r\n---\r\nBody.\r\n")
			.ShouldBe(NoteFile.Revision("---\nname: one\n---\nBody.\n"));

	[Test]
	public void Different_content_is_a_different_revision() =>
		NoteFile.Revision("one").ShouldNotBe(NoteFile.Revision("two"));

	/// <summary>
	/// The loop-termination property. Hash the whole file and writing the enrichment changes the hash
	/// of the note just enriched, so it is stale the moment it is finished and indexing never ends.
	/// </summary>
	[Test]
	public void Enriching_a_note_does_not_change_its_source_hash()
	{
		const string Before = "---\nname: one\ntags: [a]\n---\nBody.\n";

		var after = FrontmatterSplice.Apply(Before, new Dictionary<string, string?>
		{
			["dn-gist"] = "dn-gist: a summary",
			["dn-asks"] = FrontmatterSplice.Entry("dn-asks", ["why?"], "\n"),
		});

		after.ShouldNotBe(Before);
		NoteFile.SourceHash(after).ShouldBe(NoteFile.SourceHash(Before));
	}

	/// <summary>But a change a person made is exactly what should stale it.</summary>
	[Test]
	public void Editing_the_note_does_change_its_source_hash()
	{
		const string Before = "---\nname: one\n---\nBody.\n";

		NoteFile.SourceHash("---\nname: one\n---\nBody, revised.\n").ShouldNotBe(NoteFile.SourceHash(Before));
		NoteFile.SourceHash("---\nname: two\n---\nBody.\n").ShouldNotBe(NoteFile.SourceHash(Before));
	}
}
