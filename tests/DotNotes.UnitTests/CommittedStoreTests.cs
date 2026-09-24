using DotNotes.Contracts;
using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Repositories;
using DotNotes.Notes.Stores;
using DotNotes.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// That a checkout's committed notes are found wherever they are, by the index they generate, and
/// that finding one is what opts the checkout in.
/// <para>
/// A store somebody moved and a config nobody updated is the failure this guards: the notes are
/// right there, and a server that looked only where the config said would answer with none of them.
/// </para>
/// </summary>
public sealed class CommittedStoreTests
{
	private static NoteOptions Options(GitFixture fixture, string root) => new()
	{
		DefaultRoot = root,
		LocalAppData = GitFixture.Under(fixture.Root, "localappdata"),
		Environment = _ => null,
	};

	private static NoteService Service(GitFixture fixture, string root)
	{
		var options = Options(fixture, root);

		return new NoteService(options, new CrawlingNoteSearch(options));
	}

	/// <summary>A store's generated index, which is all it takes to be found.</summary>
	private static void Store(string checkout, string folder) =>
		GitFixture.Write(checkout, $"{folder}/{NoteIndexFile.FileName}", NoteIndexFile.Render("widget", []));

	[Test]
	public void A_moved_store_is_found_through_the_git_index_and_needs_no_config()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");

		Store(checkout, "docs/notes");
		GitIndexFile.Write(checkout, "docs/notes/index.md", "src/Program.cs");

		var stores = NoteStores.For(checkout, Options(fixture, checkout));

		stores.Repo.IsAvailable.ShouldBeTrue();
		stores.Repo.Path.ShouldBe(Path.Combine(checkout, "docs", "notes"));
	}

	/// <summary>The one untracked store there can be is the one just made, before its first commit, at the default path.</summary>
	[Test]
	public void An_untracked_store_at_the_default_path_is_found()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");

		Store(checkout, ".dotnotes/notes");

		NoteStores.For(checkout, Options(fixture, checkout)).Repo.IsAvailable.ShouldBeTrue();
	}

	/// <summary>A documentation site's index.md is not a store, and must not opt the checkout in.</summary>
	[Test]
	public void An_index_file_nobody_generated_is_not_a_store()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");

		GitFixture.Write(checkout, "docs/index.md", "# Widget\n\nThe documentation.\n");
		GitIndexFile.Write(checkout, "docs/index.md");

		NoteStores.For(checkout, Options(fixture, checkout)).Repo.IsAvailable.ShouldBeFalse();
	}

	/// <summary>
	/// A config naming a folder with nothing in it, beside a store somebody moved, is a config nobody
	/// updated. Writing where it says would start a second store next to the real one.
	/// </summary>
	[Test]
	public void A_found_store_beats_a_configured_folder_with_nothing_in_it()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");

		GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{"repository": "widget"}""");
		Store(checkout, "docs/notes");
		GitIndexFile.Write(checkout, "docs/notes/index.md");

		NoteStores.For(checkout, Options(fixture, checkout)).Repo.Path.ShouldBe(Path.Combine(checkout, "docs", "notes"));
	}

	/// <summary>In a monorepo, the project a session works in is the store its notes belong to.</summary>
	[Test]
	public void A_monorepo_writes_to_the_store_enclosing_the_session_and_reads_them_all()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Platform");

		Store(checkout, "api/notes");
		Store(checkout, "web/notes");
		GitIndexFile.Write(checkout, "api/notes/index.md", "web/notes/index.md");

		var inside = GitFixture.Under(checkout, "api", "notes", "drafts");
		var stores = NoteStores.For(inside, Options(fixture, inside));

		stores.Repo.Path.ShouldBe(Path.Combine(checkout, "api", "notes"));
		stores.Reading(StoreSelection.Repository).Select(store => store.Path)
			.ShouldBe([Path.Combine(checkout, "api", "notes"), Path.Combine(checkout, "web", "notes")]);
	}

	[Test]
	public void A_monorepo_session_outside_every_store_is_refused_a_write_and_told_why()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Platform");

		Store(checkout, "api/notes");
		Store(checkout, "web/notes");
		GitIndexFile.Write(checkout, "api/notes/index.md", "web/notes/index.md");

		var service = Service(fixture, checkout);

		Should.Throw<McpRefusal>(() => service.Write("one", "First", "Body.", "repository", null, null, null, null))
			.Message.ShouldContain("inside none of them");
	}

	/// <summary>A Perforce workspace has no .git, and its config is the only thing that says it is a repository.</summary>
	[Test]
	public void A_config_outside_git_declares_a_repository()
	{
		using var fixture = GitFixture.Create();
		var workspace = fixture.Plain("Depot");

		GitFixture.Write(workspace, ".dotnotes/dotnotes.json", """{"repository": "depot"}""");

		var stores = NoteStores.For(GitFixture.Under(workspace, "src"), Options(fixture, workspace));

		stores.Repository.Kind.ShouldBe(RepositoryKind.Configured);
		stores.Repository.Key.ShouldBe("depot");
		stores.Repo.Path.ShouldBe(Path.Combine(workspace, ".dotnotes", "notes"));
		Path.GetFileName(stores.Machine.Path).ShouldBe("depot");
	}

	/// <summary>Outside git the only other thing to key on is the path, which is what moving the workspace changes.</summary>
	[Test]
	public void A_config_outside_git_without_a_name_refuses()
	{
		using var fixture = GitFixture.Create();
		var workspace = fixture.Plain("Depot");

		GitFixture.Write(workspace, ".dotnotes/dotnotes.json", """{"notes": "notes"}""");

		Should.Throw<DotNotesConfigurationException>(() => NoteStores.For(workspace, Options(fixture, workspace)))
			.Message.ShouldContain("repository");
	}

	[Test]
	public void Init_opts_a_checkout_in_and_then_says_it_already_has()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");
		var service = Service(fixture, checkout);

		service.Init(checkout);

		NoteStores.For(checkout, Options(fixture, checkout)).Repo.IsAvailable.ShouldBeTrue();
		Should.Throw<McpRefusal>(() => service.Init(checkout)).Message.ShouldContain("already opted in");
	}

	/// <summary>Version 4 compresses every path against the one before, so a reader that skips ahead reads nonsense.</summary>
	[Test]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public void Every_index_version_lists_the_same_paths(int version)
	{
		var index = GitIndexFile.Build(
			[
				("api/notes/index.md", GitIndexFile.File),
				("api/notes/intro.md", GitIndexFile.File),
				("api/src/Program.cs", GitIndexFile.File),
				("index.md", GitIndexFile.File),
				("web/notes/index.md", GitIndexFile.File),
			],
			version);

		GitIndex.Named(index, "index.md").ShouldBe(["api/notes/index.md", "index.md", "web/notes/index.md"]);
	}

	[Test]
	public void A_sha256_index_is_read_with_its_own_hash_length()
	{
		var index = GitIndexFile.Build([("notes/index.md", GitIndexFile.File)], hashLength: 32);

		GitIndex.Named(index, "index.md").ShouldBe(["notes/index.md"]);
	}

	/// <summary>A submodule appears in its superproject's index as one gitlink, and is its own repository.</summary>
	[Test]
	public void A_gitlink_is_not_a_file()
	{
		var index = GitIndexFile.Build([("vendor/index.md", GitIndexFile.Gitlink)]);

		GitIndex.Named(index, "index.md").ShouldBeEmpty();
	}
}
