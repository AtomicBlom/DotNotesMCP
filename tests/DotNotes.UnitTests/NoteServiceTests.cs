using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// The tools, against real files in a real store.
/// <para>
/// Everything below goes through the same path a call from a client does: a directory is resolved
/// to a repository, both stores are located, notes are crawled off disk and ranked. What is being
/// guarded is that the parts agree with each other, which no test of one of them can show.
/// </para>
/// </summary>
public sealed class NoteServiceTests
{
	/// <summary>A store with notes in it, and the service that reads it.</summary>
	private sealed record Harness(NoteService Service, string Checkout, GitFixture Fixture) : IDisposable
	{
		public void Dispose() => Fixture.Dispose();
	}

	private static Harness Create(params (string Name, string Content)[] notes)
	{
		var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var options = new NoteOptions
		{
			DefaultRoot = checkout,
			LocalAppData = GitFixture.Under(fixture.Root, "localappdata"),

			// Nothing from the real environment: a variable set on this machine must not change
			// what a test resolves.
			Environment = _ => null,
		};

		RepositoryIdentity.Forget();

		var store = GitFixture.Under(
			MachineSettingsFile.DirectoryFor(options.LocalAppData), "notes", "rosemcp");

		foreach (var (name, content) in notes)
		{
			File.WriteAllText(Path.Combine(store, $"{name}.md"), content);
		}

		return new Harness(new NoteService(options, new CrawlingNoteSearch(options)), checkout, fixture);
	}

	private static string Note(string name, string description, string body, string? extra = null) =>
		$"---\nname: {name}\ndescription: {description}\n{extra ?? string.Empty}---\n{body}\n";

	/// <summary>
	/// The behaviour the tokenizer exists for, end to end: a note that only ever wrote the
	/// identifier is found by somebody typing the words.
	/// </summary>
	[Test]
	public void A_query_of_words_finds_a_note_that_wrote_only_the_identifier()
	{
		using var harness = Create(
			("bindable", Note("bindable", "WinUI binding", "Add GeneratedBindableCustomProperty to the view model.")),
			("unrelated", Note("unrelated", "Something else", "Nothing to do with it.")));

		var result = harness.Service.Search("bindable property", null, null, null, 10);

		result.Matches.ShouldNotBeEmpty();
		result.Matches[0].Note.Name.ShouldBe("bindable");
	}

	/// <summary>A query naming the note is the strongest signal there is, and has to win.</summary>
	[Test]
	public void A_title_match_outranks_a_body_mention()
	{
		using var harness = Create(
			("tray-dev-loop", Note("tray-dev-loop", "Running a dev tray", "Steps for the loop.")),
			("other", Note("other", "Something else", "This mentions the tray dev loop in passing.")));

		var result = harness.Service.Search("tray dev loop", null, null, null, 10);

		result.Matches[0].Note.Name.ShouldBe("tray-dev-loop");
	}

	/// <summary>A hit carries a window of the note, never the note. That is what makes searching cheap.</summary>
	[Test]
	public void A_hit_carries_an_extract_rather_than_the_note()
	{
		var body = string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 60));

		using var harness = Create(("long", Note("long", "A long note", body)));

		var result = harness.Service.Search("fox", null, null, null, 10);

		var extract = result.Matches.ShouldHaveSingleItem().Extract;

		extract.Length.ShouldBeLessThan(body.Length);
		extract.Length.ShouldBeLessThanOrEqualTo(Snippet.Length * 2);
	}

	/// <summary>No query lists, which is why there is no separate listing tool.</summary>
	[Test]
	public void No_query_lists_everything()
	{
		using var harness = Create(
			("one", Note("one", "First", "Body one.")),
			("two", Note("two", "Second", "Body two.")));

		var result = harness.Service.Search(null, null, null, null, 10);

		result.Matches.Count.ShouldBe(2);
		result.Searched.ShouldBe(2);
	}

	[Test]
	public void A_type_filter_narrows_and_an_unknown_one_refuses()
	{
		using var harness = Create(
			("a-project", Note("a-project", "Project note", "Body.", "type: project\n")),
			("a-user", Note("a-user", "User note", "Body.", "type: user\n")));

		harness.Service.Search(null, null, "user", null, 10)
			.Matches.ShouldHaveSingleItem().Note.Name.ShouldBe("a-user");

		Should.Throw<ArgumentException>(() => harness.Service.Search(null, null, "typo", null, 10))
			.Message.ShouldContain("feedback");
	}

	/// <summary>An unknown scope refuses rather than quietly searching both and finding less.</summary>
	[Test]
	public void An_unknown_scope_refuses_and_lists_what_is_allowed()
	{
		using var harness = Create(("one", Note("one", "First", "Body.")));

		Should.Throw<ArgumentException>(() => harness.Service.Search(null, "repo ository", null, null, 10))
			.Message.ShouldContain("machine, repository, both");
	}

	/// <summary>Every result says which repository answered, filled at one place rather than per tool.</summary>
	[Test]
	public void Every_result_names_the_repository_that_answered()
	{
		using var harness = Create(("one", Note("one", "First", "Body with a [[two]] link.")));

		harness.Service.Search(null, null, null, null, 10).Repository.ShouldBe("rosemcp");
		harness.Service.Read("one", null).Repository.ShouldBe("rosemcp");
		harness.Service.Context(null).Repository.ShouldBe("rosemcp");
	}

	[Test]
	public void Reading_a_note_reports_its_links_and_what_links_back()
	{
		using var harness = Create(
			("one", Note("one", "First", "Points at [[two]] and at [[missing]].")),
			("two", Note("two", "Second", "Points back at [[one]].")));

		var read = harness.Service.Read("one", null);

		read.Links.Select(link => link.Target).ShouldBe(["two", "missing"]);
		read.Links.Single(link => link.Target == "two").Resolved.ShouldBeTrue();
		read.Links.Single(link => link.Target == "missing").Resolved.ShouldBeFalse();
		read.Backlinks.ShouldHaveSingleItem().Name.ShouldBe("two");
	}

	/// <summary>A name that is not there says so, and says how to find out what is.</summary>
	[Test]
	public void Reading_a_missing_note_refuses_and_says_how_to_look()
	{
		using var harness = Create(("one", Note("one", "First", "Body.")));

		Should.Throw<McpRefusal>(() => harness.Service.Read("absent", null))
			.Message.ShouldContain("note_search");
	}

	/// <summary>
	/// A store that refused says so on the answer it could not contribute to. An empty result reads
	/// as "nothing to find", and a caller who believes that stops asking.
	/// </summary>
	[Test]
	public void A_refusing_store_is_reported_on_the_result()
	{
		using var harness = Create(("one", Note("one", "First", "Body.")));

		var result = harness.Service.Search(null, null, null, null, 10);

		result.Notices.ShouldContain(notice => notice.Contains("dotnotes.json", StringComparison.Ordinal));
	}

	/// <summary>The context tool is what makes the premise visible: the worktree and the root differ.</summary>
	[Test]
	public void Context_names_the_repository_and_how_it_resolved()
	{
		using var harness = Create();

		var context = harness.Service.Context(null);

		context.Kind.ShouldBe("Checkout");
		context.Root.ShouldBe(harness.Checkout);
		context.NamedBy.ShouldBe("OriginRemote");
		context.MachineStore.Writable.ShouldBeTrue();
		context.RepositoryStore.Writable.ShouldBeFalse();
		context.RepositoryStore.Unavailable.ShouldNotBeNull();
	}

	/// <summary>
	/// A note about another machine is shown with a flag rather than hidden: the other machine's
	/// quirk is often exactly what is being looked for.
	/// </summary>
	[Test]
	public void A_note_about_another_machine_is_flagged_not_hidden()
	{
		using var harness = Create(
			("arm-quirk", Note("arm-quirk", "ARM64 only", "Body.", "machines: [some-other-box]\n")));

		var match = harness.Service.Search(null, null, null, null, 10).Matches.ShouldHaveSingleItem();

		match.Note.Name.ShouldBe("arm-quirk");
		match.OtherMachine.ShouldBeTrue();
	}

	/// <summary>A change made in Obsidian has to be visible to the next search, not the next restart.</summary>
	[Test]
	public void A_note_added_after_the_first_search_is_found()
	{
		using var harness = Create(("one", Note("one", "First", "Body.")));

		harness.Service.Search(null, null, null, null, 10).Matches.Count.ShouldBe(1);

		var store = Path.GetDirectoryName(
			harness.Service.Search(null, null, null, null, 10).Matches[0].Note.Path)!;

		File.WriteAllText(Path.Combine(store, "two.md"), Note("two", "Second", "Body."));

		harness.Service.Search(null, null, null, null, 10).Matches.Count.ShouldBe(2);
	}
}
