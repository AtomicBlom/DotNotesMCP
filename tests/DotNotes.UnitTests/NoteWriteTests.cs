using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// Writing and removing notes, against real files.
/// <para>
/// The guards here are about losing somebody's work: an edit made in Obsidian thirty seconds ago
/// overwritten by a session that had not seen it, a note that grows until nobody finishes it, a
/// retraction that leaves every note referencing it pointing at a gap.
/// </para>
/// </summary>
public sealed class NoteWriteTests
{
	private sealed record Harness(NoteService Service, string Checkout, string Store, GitFixture Fixture)
		: IDisposable
	{
		public void Dispose() => Fixture.Dispose();
	}

	private static Harness Create(bool optIn = false)
	{
		var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		if (optIn) GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "rosemcp" }""");


		var options = new NoteOptions
		{
			DefaultRoot = checkout,
			LocalAppData = GitFixture.Under(fixture.Root, "localappdata"),

			// Nothing from the real environment: a variable set on this machine must not change
			// what a test resolves.
			Environment = _ => null,
		};

		var store = Path.Combine(
			MachineSettingsFile.DirectoryFor(options.LocalAppData), "notes", "rosemcp");

		return new Harness(
			new NoteService(options, new CrawlingNoteSearch(options)), checkout, store, fixture);
	}

	[Test]
	public void A_note_is_written_and_reads_back()
	{
		using var harness = Create();

		var written = harness.Service.Write(
			"arm64-msbuild-locator",
			"MSBuildLocator picks the x64 SDK on ARM64",
			"Pin the architecture first.",
			"machine",
			null,
			["arm64"],
			["trinity"],
			null);

		written.Created.ShouldBeTrue();
		written.Changed.ShouldBeTrue();
		written.Note.Name.ShouldBe("arm64-msbuild-locator");
		written.Scope.ShouldBe("machine");

		var read = harness.Service.Read("arm64-msbuild-locator", null);

		read.Note.Description.ShouldBe("MSBuildLocator picks the x64 SDK on ARM64");
		read.Note.Tags.ShouldBe(["arm64"]);
		read.Note.Machines.ShouldBe(["trinity"]);
		read.Body.ShouldContain("Pin the architecture first.");
	}

	/// <summary>Scope is the one decision with no undo, so there is nothing to default to.</summary>
	[Test]
	[Arguments("")]
	[Arguments(null)]
	[Arguments("Machine store")]
	public void A_write_without_a_known_scope_refuses(string? scope)
	{
		using var harness = Create();

		Should.Throw<ArgumentException>(
				() => harness.Service.Write("one", "First", "Body.", scope!, null, null, null, null))
			.Message.ShouldContain("machine, repository");
	}

	/// <summary>
	/// A repository that has not opted in is not written to, whatever the caller asks, and the
	/// refusal is the one that says how to opt in.
	/// </summary>
	[Test]
	public void A_repository_write_refuses_until_the_repository_opts_in()
	{
		using var harness = Create();

		Should.Throw<McpRefusal>(
				() => harness.Service.Write("one", "First", "Body.", "repository", null, null, null, null))
			.Message.ShouldContain("dotnotes.json");

		Directory.Exists(Path.Combine(harness.Checkout, ".dotnotes", "notes")).ShouldBeFalse();
	}

	[Test]
	public void A_repository_write_lands_in_the_working_tree_once_it_has()
	{
		using var harness = Create(optIn: true);

		var written = harness.Service.Write(
			"tunit-opt-in", "Why global.json is here", "Body.", "repository", null, null, null, null);

		written.Note.Path.ShouldStartWith(Path.Combine(harness.Checkout, ".dotnotes", "notes"));
		File.Exists(written.Note.Path).ShouldBeTrue();
	}

	/// <summary>
	/// The hand-edit guard. These are files a person edits in Obsidian while a session is running, so
	/// replacing one blind is a way to lose an edit and never find out.
	/// </summary>
	[Test]
	public void Replacing_a_note_without_its_revision_refuses()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		Should.Throw<McpRefusal>(
				() => harness.Service.Write("one", "Second", "Body.", "machine", null, null, null, null))
			.Message.ShouldContain("revision");
	}

	[Test]
	public void Replacing_a_note_with_its_revision_works()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		var revision = harness.Service.Read("one", null).Note.Revision;
		var written = harness.Service.Write(
			"one", "Second", "New body.", "machine", null, null, null, revision);

		written.Created.ShouldBeFalse();
		written.Changed.ShouldBeTrue();
		harness.Service.Read("one", null).Note.Description.ShouldBe("Second");
	}

	/// <summary>A stale revision is exactly the edit this exists to protect, so it names both.</summary>
	[Test]
	public void Replacing_a_note_that_changed_underneath_refuses()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		var stale = harness.Service.Read("one", null).Note.Revision;
		var path = harness.Service.Read("one", null).Note.Path;

		File.WriteAllText(path, "---\nname: one\ndescription: Edited by hand\n---\nTheir words.\n");

		Should.Throw<McpRefusal>(
				() => harness.Service.Write("one", "Mine", "My words.", "machine", null, null, null, stale))
			.Message.ShouldContain("changed since");

		File.ReadAllText(path).ShouldContain("Their words.");
	}

	/// <summary>
	/// One real memory in the store this replaces is 41 KB. A ceiling is enforceable only because
	/// there is no append, so a note cannot creep past it one call at a time.
	/// </summary>
	[Test]
	public void A_note_over_the_ceiling_refuses_and_says_where_to_split_it()
	{
		using var harness = Create();
		var body = "# First part\n\n" + new string('x', 7000) + "\n\n# Second part\n\nmore";

		var refusal = Should.Throw<McpRefusal>(
			() => harness.Service.Write("long", "Too long", body, "machine", null, null, null, null));

		refusal.Message.ShouldContain("# First part");
		refusal.Message.ShouldContain("# Second part");
	}

	/// <summary>On a synced store a no-op write is a replication and a stored revision.</summary>
	[Test]
	public void Writing_the_same_note_again_changes_nothing()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		var revision = harness.Service.Read("one", null).Note.Revision;
		var again = harness.Service.Write("one", "First", "Body.", "machine", null, null, null, revision);

		again.Changed.ShouldBeFalse();
	}

	/// <summary>A person's own properties and comments survive a note being replaced.</summary>
	[Test]
	public void Replacing_a_note_keeps_what_the_person_added_to_it()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		var path = harness.Service.Read("one", null).Note.Path;
		var theirs = File.ReadAllText(path).Replace(
			"name: one", "# mine\nname: one\nmy-own-property: keep me", StringComparison.Ordinal);

		File.WriteAllText(path, theirs);

		var revision = harness.Service.Read("one", null).Note.Revision;

		harness.Service.Write("one", "Second", "New body.", "machine", null, null, null, revision);

		var after = File.ReadAllText(path);

		after.ShouldContain("# mine");
		after.ShouldContain("my-own-property: keep me");
		after.ShouldContain("description: Second");
	}

	[Test]
	public void Deleting_reports_whether_there_was_a_note()
	{
		using var harness = Create();

		harness.Service.Delete("absent", "machine").Existed.ShouldBeFalse();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);

		harness.Service.Delete("one", "machine").Existed.ShouldBeTrue();
		Should.Throw<McpRefusal>(() => harness.Service.Read("one", null));
	}

	/// <summary>A retraction that silently breaks the notes referencing it is how a store rots.</summary>
	[Test]
	public void Deleting_reports_what_now_links_to_nothing()
	{
		using var harness = Create();

		harness.Service.Write("one", "First", "Body.", "machine", null, null, null, null);
		harness.Service.Write("two", "Second", "Points at [[one]].", "machine", null, null, null, null);

		var deleted = harness.Service.Delete("one", "machine");

		deleted.LeftDangling.ShouldHaveSingleItem().Name.ShouldBe("two");
	}

	/// <summary>The index cannot disagree with the notes, because a write regenerates it.</summary>
	[Test]
	public void A_write_regenerates_the_index_beside_it()
	{
		using var harness = Create();

		harness.Service.Write("one", "The first note", "Body.", "machine", null, null, null, null);

		var index = File.ReadAllText(Path.Combine(harness.Store, NoteIndexFile.FileName));

		index.ShouldContain("[[one]]");
		index.ShouldContain("The first note");
		NoteIndexFile.IsGenerated(index).ShouldBeTrue();

		harness.Service.Delete("one", "machine");

		File.ReadAllText(Path.Combine(harness.Store, NoteIndexFile.FileName))
			.ShouldNotContain("[[one]]");
	}

	/// <summary>The generated index is not itself a note, or a listing of everything matches everything.</summary>
	[Test]
	public void The_index_is_not_searchable_as_a_note()
	{
		using var harness = Create();

		harness.Service.Write("one", "The first note", "Body.", "machine", null, null, null, null);

		harness.Service.Search(null, null, null, null, 10).Searched.ShouldBe(1);
	}

	/// <summary>
	/// An empty store and a store with nothing matching look identical, and a caller who reads the
	/// first as the second concludes there is nothing to find and stops asking.
	/// </summary>
	[Test]
	public void An_empty_store_says_it_is_empty_and_what_to_do()
	{
		using var harness = Create();

		var result = harness.Service.Search("anything", null, null, null, 10);

		result.Searched.ShouldBe(0);
		result.Notices.ShouldContain(notice => notice.Contains("note_write", StringComparison.Ordinal));
	}
}
