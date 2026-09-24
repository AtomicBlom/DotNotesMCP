using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// That a repository whose key changes keeps its notes: they are found through the evidence, read,
/// reported as waiting to move, and moved only when somebody says so.
/// <para>
/// Every case here is a person doing something ordinary -- adding an origin, moving the folder,
/// cloning a fork, merging three repositories into one -- and every one of them, without the
/// evidence, gives an answer of no notes that reads exactly like a repository nobody has written
/// notes for.
/// </para>
/// </summary>
public sealed class PendingMoveTests
{
	private sealed class Harness : IDisposable
	{
		public GitFixture Fixture { get; } = GitFixture.Create();

		public string LocalAppData => GitFixture.Under(Fixture.Root, "localappdata");

		public string MachineRoot => Path.Combine(MachineSettingsFile.DirectoryFor(LocalAppData), "notes");

		public NoteOptions Options(string root) => new()
		{
			DefaultRoot = root,
			LocalAppData = LocalAppData,
			Environment = _ => null,
		};

		/// <summary>A service answering from one directory, sharing this machine's evidence with every other.</summary>
		public NoteService Service(string root)
		{
			var options = Options(root);

			return new NoteService(options, new CrawlingNoteSearch(options));
		}

		public NoteStores Stores(string root) => NoteStores.For(root, Options(root));

		public void Dispose() => Fixture.Dispose();
	}

	private static void Remember(NoteService service, string name) =>
		service.Write(name, $"About {name}", $"What was learned about {name}.", "machine", null, null, null, null);

	/// <summary>
	/// The case that started this: a repository named by its folder is given an origin, and its key
	/// moves from the hashed folder name to the remote's.
	/// </summary>
	[Test]
	public void Adding_an_origin_keeps_writing_to_the_store_that_has_the_notes()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.CheckoutWithoutRemote("Widget");

		Remember(harness.Service(checkout), "build-quirk");

		var before = harness.Stores(checkout).Machine.Path;

		GitFixture.SetRemote(checkout, "https://github.com/AtomicBlom/Widget.git");

		var service = harness.Service(checkout);
		var search = service.Search(null, null, null, null, 10);

		search.Matches.Select(match => match.Note.Name).ShouldBe(["build-quirk"]);
		search.Notices.ShouldContain(notice => notice.Contains("--adopt") && notice.Contains("'atomicblom-widget'"));

		Remember(service, "second-quirk");

		File.Exists(Path.Combine(before, "second-quirk.md")).ShouldBeTrue();
		Directory.Exists(Path.Combine(harness.MachineRoot, "atomicblom-widget")).ShouldBeFalse();
	}

	[Test]
	public void Adopting_renames_the_store_and_the_notice_goes()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.CheckoutWithoutRemote("Widget");

		Remember(harness.Service(checkout), "build-quirk");
		GitFixture.SetRemote(checkout, "https://github.com/AtomicBlom/Widget.git");

		harness.Service(checkout).Adopt(checkout);

		var after = harness.Service(checkout).Search(null, null, null, null, 10);

		after.Matches.Select(match => match.Note.Name).ShouldBe(["build-quirk"]);
		after.Notices.ShouldNotContain(notice => notice.Contains("--adopt"));
		File.Exists(Path.Combine(harness.MachineRoot, "atomicblom-widget", "build-quirk.md")).ShouldBeTrue();
		harness.Stores(checkout).Pending.ShouldBeNull();
	}

	/// <summary>
	/// A repository with no remote is keyed by a hash of its path, so moving it is a new key. Its git
	/// directory has moved too, which leaves the root commits as the only evidence -- and the old
	/// checkout being gone is what makes that evidence enough to write to.
	/// </summary>
	[Test]
	public void A_moved_repository_is_recognised_by_its_roots()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.CheckoutWithoutRemote("Widget");

		GitFixture.CommitGraph(checkout, GitFixture.Commit(1));
		Remember(harness.Service(checkout), "build-quirk");

		var before = harness.Stores(checkout).Machine.Path;
		var moved = Path.Combine(GitFixture.Under(harness.Fixture.Root, "elsewhere"), "Widget");

		Directory.Move(checkout, moved);

		var stores = harness.Stores(moved);

		stores.Pending.ShouldNotBeNull();
		stores.Pending.Redirected.ShouldBeTrue();
		stores.Machine.Path.ShouldBe(before);
	}

	/// <summary>
	/// A fork's clone shares its upstream's roots while the upstream's checkout is still here. That
	/// store is read, because its notes are usually relevant, and never written, because it is not
	/// this repository's.
	/// </summary>
	[Test]
	public void A_fork_reads_its_upstreams_store_and_writes_its_own()
	{
		using var harness = new Harness();
		var upstream = harness.Fixture.Checkout("Widget", "https://github.com/AtomicBlom/Widget.git");
		var fork = harness.Fixture.Checkout("WidgetFork", "https://github.com/someone/WidgetFork.git");

		GitFixture.CommitGraph(upstream, GitFixture.Commit(1));
		GitFixture.CommitGraph(fork, GitFixture.Commit(1));
		Remember(harness.Service(upstream), "upstream-quirk");

		var service = harness.Service(fork);
		var search = service.Search(null, null, null, null, 10);

		search.Matches.Select(match => match.Note.Name).ShouldBe(["upstream-quirk"]);
		search.Notices.ShouldContain(notice => notice.Contains("never written"));

		Remember(service, "fork-quirk");

		File.Exists(Path.Combine(harness.MachineRoot, "someone-widgetfork", "fork-quirk.md")).ShouldBeTrue();
		File.Exists(Path.Combine(harness.MachineRoot, "atomicblom-widget", "fork-quirk.md")).ShouldBeFalse();
	}

	[Test]
	public void A_dismissed_store_is_no_longer_offered()
	{
		using var harness = new Harness();
		var upstream = harness.Fixture.Checkout("Widget", "https://github.com/AtomicBlom/Widget.git");
		var fork = harness.Fixture.Checkout("WidgetFork", "https://github.com/someone/WidgetFork.git");

		GitFixture.CommitGraph(upstream, GitFixture.Commit(1));
		GitFixture.CommitGraph(fork, GitFixture.Commit(1));
		Remember(harness.Service(upstream), "upstream-quirk");

		harness.Service(fork).Dismiss(fork);

		harness.Stores(fork).Pending.ShouldBeNull();
		harness.Service(fork).Search(null, null, null, null, 10).Matches.ShouldBeEmpty();
	}

	/// <summary>
	/// Three repositories merged into one monorepo: its roots intersect all three stores, so all
	/// three are read, and adopting merges them.
	/// </summary>
	[Test]
	public void A_monorepo_reads_every_store_its_roots_match_and_merges_them_on_adoption()
	{
		using var harness = new Harness();
		string[] parts = ["Api", "Web", "Tools"];

		for (var i = 0; i < parts.Length; i++)
		{
			var part = harness.Fixture.Checkout(parts[i]);

			GitFixture.CommitGraph(part, GitFixture.Commit(i + 1));
			Remember(harness.Service(part), $"{parts[i].ToLowerInvariant()}-quirk");
			Directory.Delete(part, recursive: true);
		}

		var monorepo = harness.Fixture.Checkout("Platform");
		GitFixture.CommitGraph(monorepo, GitFixture.Commit(1), GitFixture.Commit(2), GitFixture.Commit(3));

		var service = harness.Service(monorepo);

		service.Search(null, null, null, null, 10).Matches.Count.ShouldBe(3);
		harness.Stores(monorepo).Pending!.Redirected.ShouldBeFalse();

		service.Adopt(monorepo);

		Directory.EnumerateFiles(Path.Combine(harness.MachineRoot, "atomicblom-platform"), "*-quirk.md").Count().ShouldBe(3);
		Directory.Exists(Path.Combine(harness.MachineRoot, "atomicblom-api")).ShouldBeFalse();
		harness.Stores(monorepo).Pending.ShouldBeNull();
	}

	/// <summary>
	/// Two notes claiming one name with different contents are each somebody's note. A merge that
	/// chose one would overwrite the other; a merge that stopped halfway would leave both stores
	/// half-moved. So it refuses before moving anything.
	/// </summary>
	[Test]
	public void A_merge_with_a_collision_moves_nothing()
	{
		using var harness = new Harness();
		string[] parts = ["Api", "Web"];

		for (var i = 0; i < parts.Length; i++)
		{
			var part = harness.Fixture.Checkout(parts[i]);

			GitFixture.CommitGraph(part, GitFixture.Commit(i + 1));
			harness.Service(part).Write("shared", "Differs", $"From {parts[i]}.", "machine", null, null, null, null);
			Directory.Delete(part, recursive: true);
		}

		var monorepo = harness.Fixture.Checkout("Platform");
		GitFixture.CommitGraph(monorepo, GitFixture.Commit(1), GitFixture.Commit(2));

		Should.Throw<McpRefusal>(() => harness.Service(monorepo).Adopt(monorepo)).Message.ShouldContain("shared.md");

		File.Exists(Path.Combine(harness.MachineRoot, "atomicblom-api", "shared.md")).ShouldBeTrue();
		File.Exists(Path.Combine(harness.MachineRoot, "atomicblom-web", "shared.md")).ShouldBeTrue();
		Directory.Exists(Path.Combine(harness.MachineRoot, "atomicblom-platform")).ShouldBeFalse();
	}

	/// <summary>Writing a name that is already a note in a store waiting to move would split it in two.</summary>
	[Test]
	public void A_write_to_a_name_held_by_a_waiting_store_is_refused()
	{
		using var harness = new Harness();
		var upstream = harness.Fixture.Checkout("Widget", "https://github.com/AtomicBlom/Widget.git");
		var fork = harness.Fixture.Checkout("WidgetFork", "https://github.com/someone/WidgetFork.git");

		GitFixture.CommitGraph(upstream, GitFixture.Commit(1));
		GitFixture.CommitGraph(fork, GitFixture.Commit(1));
		Remember(harness.Service(upstream), "quirk");

		Should.Throw<McpRefusal>(() => Remember(harness.Service(fork), "quirk")).Message.ShouldContain("--adopt");
	}

	/// <summary>
	/// A damaged evidence file is reported and otherwise ignored. Refusing to serve notes over a cache
	/// would take the server down for a feature whose failure is no worse than not having it -- and
	/// writing over it would destroy whatever a person might still recover from it.
	/// </summary>
	[Test]
	public void An_unreadable_evidence_file_is_reported_and_left_alone()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.Checkout("Widget");
		var evidence = GitFixture.Write(MachineSettingsFile.DirectoryFor(harness.LocalAppData), RepositoryEvidence.FileName, "{ not json");

		var service = harness.Service(checkout);

		Remember(service, "quirk");

		service.Search(null, null, null, null, 10).Notices.ShouldContain(notice => notice.Contains(RepositoryEvidence.FileName));
		File.ReadAllText(evidence).ShouldBe("{ not json");
	}

	/// <summary>
	/// A remote-named repository's notes are under its short name wherever a server that keyed on the
	/// last segment wrote them, with nothing recorded about that store. It is found anyway, written to
	/// while nothing else claims it, and renamed on adoption.
	/// </summary>
	[Test]
	public void A_store_under_the_short_name_is_found_and_adopted()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.Checkout("RoseMCP");
		var shortName = GitFixture.Under(harness.MachineRoot, "rosemcp");

		File.WriteAllText(Path.Combine(shortName, "quirk.md"), "---\nname: quirk\ndescription: A quirk\n---\nBody.\n");

		var stores = harness.Stores(checkout);

		stores.Pending!.Redirected.ShouldBeTrue();
		stores.Machine.Path.ShouldBe(shortName);

		harness.Service(checkout).Adopt(checkout);

		File.Exists(Path.Combine(harness.MachineRoot, "atomicblom-rosemcp", "quirk.md")).ShouldBeTrue();
		harness.Stores(checkout).Pending.ShouldBeNull();
	}

	/// <summary>
	/// Two repositories that shared a short name both see that store. Once one has been seen using it,
	/// it is the other's to read and never to write, which keeps two repositories out of one store.
	/// </summary>
	[Test]
	public void A_short_name_store_another_repository_uses_is_only_read()
	{
		using var harness = new Harness();
		var mine = harness.Fixture.Checkout("mine", "https://github.com/AtomicBlom/tools.git");
		var theirs = harness.Fixture.Checkout("theirs", "https://github.com/someone/tools.git");

		GitFixture.Under(harness.MachineRoot, "tools");
		Remember(harness.Service(mine), "mine-quirk");

		var stores = harness.Stores(theirs);

		stores.Pending!.Redirected.ShouldBeFalse();
		Path.GetFileName(stores.Machine.Path).ShouldBe("someone-tools");
		harness.Service(theirs).Search(null, null, null, null, 10).Matches.Select(match => match.Note.Name).ShouldBe(["mine-quirk"]);
	}

	/// <summary>A key with no store has nothing to be found again, so looking at it records nothing.</summary>
	[Test]
	public void A_repository_with_no_notes_leaves_no_evidence()
	{
		using var harness = new Harness();
		var checkout = harness.Fixture.Checkout("Widget");

		harness.Service(checkout).Search(null, null, null, null, 10);

		File.Exists(RepositoryEvidence.PathFor(harness.LocalAppData)).ShouldBeFalse();
	}
}
