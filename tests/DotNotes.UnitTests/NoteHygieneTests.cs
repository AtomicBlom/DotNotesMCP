using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// Renaming, moving, and finding what has gone wrong.
/// <para>
/// A rename that leaves its links behind is worse than no rename, because every note that
/// referenced the old name now points at nothing and Obsidian renders each of those as an
/// invitation to create a note that already exists. Following the links is what makes renaming
/// safe enough to do.
/// </para>
/// </summary>
public sealed class NoteHygieneTests
{
	private sealed record Harness(NoteService Service, string Checkout, string Store, GitFixture Fixture)
		: IDisposable
	{
		public void Dispose() => Fixture.Dispose();
	}

	private static Harness Create(bool optIn = true)
	{
		var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		if (optIn) GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "rosemcp" }""");

		RepositoryIdentity.Forget();

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

	private static void Write(Harness harness, string name, string body, string scope = "machine") =>
		harness.Service.Write(name, $"About {name}", body, scope, null, null, null, null);

	[Test]
	public void Renaming_a_note_follows_the_links_to_it()
	{
		using var harness = Create();

		Write(harness, "old-name", "The note itself.");
		Write(harness, "pointer", "See [[old-name]] and [[old-name|the old one]].");

		var moved = harness.Service.Move("old-name", "new-name", null);

		moved.FromName.ShouldBe("old-name");
		moved.Note.Name.ShouldBe("new-name");
		moved.LinksRewritten.ShouldBe(2);

		var pointer = harness.Service.Read("pointer", null).Body;

		pointer.ShouldContain("[[new-name]]");
		pointer.ShouldContain("[[new-name|the old one]]");
		pointer.ShouldNotContain("old-name");
	}

	/// <summary>The alias is what the author wanted the link to read as; a rename has no view on it.</summary>
	[Test]
	public void An_alias_survives_a_rename()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");
		Write(harness, "pointer", "See [[one|the first thing]].");

		harness.Service.Move("one", "two", null);

		harness.Service.Read("pointer", null).Body.ShouldContain("[[two|the first thing]]");
	}

	/// <summary>
	/// A move between stores changes what a link has to say. Once a note is committed, a private
	/// note linking to it bare resolves to nothing.
	/// </summary>
	[Test]
	public void Promoting_a_note_re_prefixes_the_links_from_the_other_store()
	{
		using var harness = Create();

		Write(harness, "quirk", "The quirk itself.");
		Write(harness, "pointer", "See [[quirk]].");

		var moved = harness.Service.Move("quirk", null, "repository");

		moved.Scope.ShouldBe("repository");
		moved.Note.Path.ShouldStartWith(Path.Combine(harness.Checkout, ".dotnotes", "notes"));

		// The linking note stayed private, so its link now has to name the other store.
		harness.Service.Read("pointer", null).Body.ShouldContain("[[repo:quirk]]");
	}

	/// <summary>And the other direction drops the prefix, because the two are in one store again.</summary>
	[Test]
	public void Demoting_a_note_drops_the_prefix_again()
	{
		using var harness = Create();

		Write(harness, "quirk", "The quirk itself.", "repository");
		Write(harness, "pointer", "See [[repo:quirk]].");

		harness.Service.Move("quirk", null, "machine");

		var pointer = harness.Service.Read("pointer", null).Body;

		pointer.ShouldContain("[[quirk]]");
		pointer.ShouldNotContain("repo:quirk");
	}

	/// <summary>A qualified link resolves to the same note, so a rename has to catch it too.</summary>
	[Test]
	public void A_folder_qualified_link_is_followed()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");
		Write(harness, "pointer", "See [[rosemcp/one]].");

		harness.Service.Move("one", "two", null).LinksRewritten.ShouldBe(1);

		harness.Service.Read("pointer", null).Body.ShouldContain("[[two]]");
	}

	[Test]
	public void Moving_onto_an_existing_name_refuses()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");
		Write(harness, "two", "Body.");

		Should.Throw<McpRefusal>(() => harness.Service.Move("one", "two", null))
			.Message.ShouldContain("already exists");
	}

	[Test]
	public void Moving_nowhere_refuses_and_says_what_to_give()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");

		Should.Throw<McpRefusal>(() => harness.Service.Move("one", "one", "machine"))
			.Message.ShouldContain("toName");
	}

	[Test]
	public void Moving_a_note_that_is_not_there_refuses() =>
		Should.Throw<McpRefusal>(() => Create().Service.Move("absent", "other", null))
			.Message.ShouldContain("note_search");

	/// <summary>Promotion into a repository that has not opted in is still refused.</summary>
	[Test]
	public void Promoting_into_a_repository_without_the_opt_in_refuses()
	{
		using var harness = Create(optIn: false);

		Write(harness, "one", "Body.");

		Should.Throw<McpRefusal>(() => harness.Service.Move("one", null, "repository"))
			.Message.ShouldContain("dotnotes.json");
	}

	[Test]
	public void A_clean_store_reports_nothing_and_says_how_much_it_looked_at()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");
		Write(harness, "two", "Points at [[one]].");

		var report = harness.Service.Check(null);

		report.Problems.ShouldBeEmpty();
		report.Checked.ShouldBe(2);
	}

	[Test]
	public void A_dangling_link_is_reported()
	{
		using var harness = Create();

		Write(harness, "one", "Points at [[nowhere]].");

		var problem = harness.Service.Check(null).Problems.ShouldHaveSingleItem();

		problem.Kind.ShouldBe("dangling-link");
		problem.Note.ShouldBe("one");
		problem.Detail.ShouldContain("nowhere");
	}

	/// <summary>
	/// Orphans are deliberately absent. A note nothing links to is the ordinary state of most notes,
	/// and a report that is mostly noise is one people stop reading.
	/// </summary>
	[Test]
	public void A_note_nothing_links_to_is_not_a_problem()
	{
		using var harness = Create();

		Write(harness, "alone", "Linked from nowhere.");

		harness.Service.Check(null).Problems.ShouldBeEmpty();
	}

	[Test]
	public void A_note_edited_past_the_ceiling_by_hand_is_reported()
	{
		using var harness = Create();

		Write(harness, "one", "Short.");

		var path = harness.Service.Read("one", null).Note.Path;

		File.WriteAllText(path, "---\nname: one\n---\n" + new string('x', 9000));

		harness.Service.Check(null).Problems.ShouldContain(problem => problem.Kind == "oversized");
	}

	/// <summary>
	/// A sync service names a conflicted copy by appending a number, and it arrives as an extra note
	/// claiming the same name as the original.
	/// </summary>
	[Test]
	public void A_sync_conflict_copy_is_reported()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");
		File.Copy(
			Path.Combine(harness.Store, "one.md"),
			Path.Combine(harness.Store, "one (1).md"));

		var problems = harness.Service.Check(null).Problems;

		problems.ShouldContain(problem => problem.Kind == "sync-conflict");
		problems.ShouldContain(problem => problem.Kind == "duplicate-name");
	}

	[Test]
	public void Frontmatter_that_no_longer_parses_is_reported()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");

		File.WriteAllText(
			harness.Service.Read("one", null).Note.Path,
			"---\nname: one\nbroken: [unclosed\n  : :\n---\nBody.\n");

		harness.Service.Check(null).Problems
			.ShouldContain(problem => problem.Kind == "unreadable-frontmatter");
	}

	/// <summary>A note moved between stores by hand disagrees with itself about where it is.</summary>
	[Test]
	public void A_note_filed_against_its_own_scope_is_reported()
	{
		using var harness = Create();

		Write(harness, "one", "Body.");

		var path = harness.Service.Read("one", null).Note.Path;

		File.WriteAllText(path, File.ReadAllText(path)
			.Replace("scope: machine", "scope: repository", StringComparison.Ordinal));

		harness.Service.Check(null).Problems
			.ShouldContain(problem => problem.Kind == "misfiled");
	}

	[Test]
	public void Checking_one_store_looks_only_at_it()
	{
		using var harness = Create();

		Write(harness, "private-one", "Points at [[nowhere]].");
		Write(harness, "committed-one", "Fine.", "repository");

		harness.Service.Check("repository").Checked.ShouldBe(1);
		harness.Service.Check("repository").Problems.ShouldBeEmpty();
		harness.Service.Check("machine").Problems.ShouldNotBeEmpty();
	}
}
