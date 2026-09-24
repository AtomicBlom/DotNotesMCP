using DotNotes.Contracts;
using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// That a note kept in both stores is one note: written once, found once, updated toward the machine
/// and never away, and retired in a way that reaches the private copies git cannot.
/// </summary>
public sealed class PairTests
{
	private sealed class Harness : IDisposable
	{
		public Harness()
		{
			Checkout = Fixture.Checkout("Widget");
			GitFixture.Write(Checkout, ".dotnotes/dotnotes.json", """{"repository": "widget"}""");
		}

		public GitFixture Fixture { get; } = GitFixture.Create();

		public string Checkout { get; }

		public string LocalAppData => GitFixture.Under(Fixture.Root, "localappdata");

		public string Committed(string name) => Path.Combine(Checkout, ".dotnotes", "notes", $"{name}.md");

		public string Private(string name) =>
			Path.Combine(MachineSettingsFile.DirectoryFor(LocalAppData), "notes", "widget", $"{name}.md");

		public NoteService Service(string? root = null)
		{
			var options = new NoteOptions
			{
				DefaultRoot = root ?? Checkout,
				LocalAppData = LocalAppData,
				Environment = _ => null,
			};

			return new NoteService(options, new CrawlingNoteSearch(options));
		}

		public void Dispose() => Fixture.Dispose();
	}

	private static NoteWritten Write(NoteService service, string scope, string body = "Every branch hits this.", string? revision = null) =>
		service.Write("build-quirk", "The build quirk", body, scope, null, null, null, revision);

	private static string? IdIn(string path) =>
		NoteFrontmatter.Parse(FrontmatterBlock.Split(File.ReadAllText(path)).Yaml).Scalar(NoteFrontmatter.IdKey);

	[Test]
	public void Both_writes_one_note_into_each_store_joined_by_one_id()
	{
		using var harness = new Harness();

		var written = Write(harness.Service(), "both");

		written.Note.Scope.ShouldBe(NoteScope.Repository);
		written.Note.Twin.ShouldBe(NoteScope.Machine);
		IdIn(harness.Committed("build-quirk")).ShouldNotBeNull();
		IdIn(harness.Private("build-quirk")).ShouldBe(IdIn(harness.Committed("build-quirk")));
	}

	/// <summary>A pair listed twice is one fact taking two of ten places.</summary>
	[Test]
	public void A_pair_is_one_hit_and_the_committed_copy_answers()
	{
		using var harness = new Harness();
		var service = harness.Service();

		Write(service, "both");

		var result = service.Search(null, null, null, null, 10);

		result.Matches.Count.ShouldBe(1);
		result.Searched.ShouldBe(1);
		result.Matches[0].Note.Scope.ShouldBe(NoteScope.Repository);
		result.Matches[0].Note.Twin.ShouldBe(NoteScope.Machine);
		service.Read("build-quirk", null).Note.Scope.ShouldBe(NoteScope.Repository);
	}

	/// <summary>The point of a pair: a worktree whose branch does not have the committed note still finds it.</summary>
	[Test]
	public void A_worktree_without_the_committed_copy_finds_the_private_one()
	{
		using var harness = new Harness();

		Write(harness.Service(), "both");

		var worktree = harness.Fixture.LinkedWorktree(harness.Checkout, "Older");
		var hits = harness.Service(worktree).Search(null, null, null, null, 10).Matches;

		hits.Select(hit => hit.Note.Name).ShouldBe(["build-quirk"]);
		hits[0].Note.Scope.ShouldBe(NoteScope.Machine);
	}

	/// <summary>Making a fact private cannot publish anything, so a committed update follows into the private copy.</summary>
	[Test]
	public void Writing_the_committed_copy_updates_the_private_one()
	{
		using var harness = new Harness();
		var service = harness.Service();
		var first = Write(service, "both");

		Write(service, "repository", "Every branch hits this, and here is the fix.", first.Note.Revision);

		File.ReadAllText(harness.Private("build-quirk")).ShouldContain("here is the fix");
	}

	/// <summary>An update that reached the committed copy without being asked to is a publication nobody chose.</summary>
	[Test]
	public void Writing_the_private_copy_leaves_the_committed_one_alone()
	{
		using var harness = new Harness();
		var worktree = harness.Fixture.LinkedWorktree(harness.Checkout, "Older");

		Write(harness.Service(), "both");

		var elsewhere = harness.Service(worktree);
		var kept = elsewhere.Read("build-quirk", "machine");

		Write(elsewhere, "machine", "A private addition.", kept.Note.Revision);

		File.ReadAllText(harness.Committed("build-quirk")).ShouldNotContain("A private addition.");
	}

	/// <summary>A private copy the person has edited is theirs, and is reported rather than overwritten.</summary>
	[Test]
	public void An_edited_private_copy_is_left_alone_and_reported()
	{
		using var harness = new Harness();
		var service = harness.Service();
		var first = Write(service, "both");
		var kept = harness.Private("build-quirk");

		File.WriteAllText(kept, File.ReadAllText(kept).Replace("Every branch hits this.", "Edited by hand."));

		var second = Write(service, "repository", "The committed update.", first.Note.Revision);

		second.Notices.ShouldContain(notice => notice.Contains("left as it is"));
		File.ReadAllText(kept).ShouldContain("Edited by hand.");
		service.Check(null).Problems.Select(problem => problem.Kind).ShouldContain("twin-differs");
	}

	/// <summary>The two copies share a name because they are one note, which is not a duplicate.</summary>
	[Test]
	public void A_pair_is_not_a_duplicate_name()
	{
		using var harness = new Harness();
		var service = harness.Service();

		Write(service, "both");

		service.Check(null).Problems.ShouldBeEmpty();
	}

	/// <summary>
	/// Deleting a committed note kept in both supersedes it: the file travels by git to every machine
	/// that holds a private copy, which a deletion could never do.
	/// </summary>
	[Test]
	public void Deleting_a_pair_supersedes_the_committed_copy_and_removes_the_private_one()
	{
		using var harness = new Harness();
		var service = harness.Service();

		Write(service, "both");

		var deleted = service.Delete("build-quirk", "repository");

		deleted.Superseded.ShouldBeTrue();
		File.Exists(harness.Committed("build-quirk")).ShouldBeTrue();
		File.Exists(harness.Private("build-quirk")).ShouldBeFalse();
		service.Search(null, null, null, null, 10).Matches.ShouldBeEmpty();
		service.Read("build-quirk", null).Note.Superseded.ShouldNotBeNull();
		service.Check(null).Problems.Select(problem => problem.Kind).ShouldContain("superseded");
	}

	[Test]
	public void Deleting_a_committed_note_kept_in_one_store_deletes_it()
	{
		using var harness = new Harness();
		var service = harness.Service();

		Write(service, "repository");

		service.Delete("build-quirk", "repository").Superseded.ShouldBeFalse();
		File.Exists(harness.Committed("build-quirk")).ShouldBeFalse();
	}

	/// <summary>
	/// A retirement pulled in from somebody else's machine reaches this one's private copy the first
	/// time a checkout that can see it is used.
	/// </summary>
	[Test]
	public void A_superseded_committed_copy_retires_the_private_one()
	{
		using var harness = new Harness();

		Write(harness.Service(), "both");

		var committed = harness.Committed("build-quirk");
		File.WriteAllText(committed, File.ReadAllText(committed).Replace("---\nname:", "---\nsuperseded: \"[[better-quirk]]\"\nname:"));

		harness.Service().Search(null, null, null, null, 10);

		File.ReadAllText(harness.Private("build-quirk")).ShouldContain("superseded:");

		var worktree = harness.Fixture.LinkedWorktree(harness.Checkout, "Older");
		harness.Service(worktree).Search(null, null, null, null, 10).Matches.ShouldBeEmpty();
	}

	[Test]
	public void Two_unrelated_notes_sharing_a_name_are_not_made_one()
	{
		using var harness = new Harness();
		var service = harness.Service();

		Write(service, "repository", "The committed one.");
		Write(service, "machine", "A private one.");

		Should.Throw<McpRefusal>(() => Write(service, "both", "Merged?")).Message.ShouldContain("two different notes");
	}

	/// <summary>The pairing id is not enrichment, and counting it would mark every pair as indexed.</summary>
	[Test]
	public void The_pairing_id_does_not_make_a_note_enriched()
	{
		using var harness = new Harness();

		Write(harness.Service(), "both").Note.Enriched.ShouldBeFalse();
	}

	[Test]
	public void The_machine_index_lists_a_pair_once()
	{
		using var harness = new Harness();

		Write(harness.Service(), "both");

		var index = File.ReadAllText(Path.Combine(Path.GetDirectoryName(harness.Private("build-quirk"))!, NoteIndexFile.FileName));

		index.Split("[[build-quirk]]").Length.ShouldBe(2);
	}
}
