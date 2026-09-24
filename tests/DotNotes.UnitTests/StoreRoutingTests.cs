using DotNotes.Contracts;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;

namespace DotNotes.UnitTests;

/// <summary>
/// That a note lands where the caller asked, and that a store which cannot be used says so instead
/// of answering with nothing.
/// <para>
/// The second half is the one worth guarding. A store that is silently empty reads exactly like a
/// store with nothing in it, and a caller who gets one goes back to reading files and does not come
/// back -- so every way a store can be unavailable has to arrive as a refusal that names the fix.
/// </para>
/// </summary>
public sealed class StoreRoutingTests
{
	/// <summary>Options pointing everything at a directory the test owns and can delete.</summary>
	private static NoteOptions Options(GitFixture fixture, string? machineStore = null) => new()
	{
		LocalAppData = GitFixture.Under(fixture.Root, "localappdata"),
		MachineStore = machineStore,

		// Nothing from the real environment, so a variable set on the machine running the suite
		// cannot change what a test resolves.
		Environment = _ => null,
	};

	[Test]
	public void A_repository_files_machine_notes_under_its_key()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var stores = NoteStores.For(checkout, Options(fixture));

		stores.Machine.IsAvailable.ShouldBeTrue();
		Path.GetFileName(stores.Machine.Path).ShouldBe("atomicblom-rosemcp");
	}

	/// <summary>
	/// A directory outside git is not a durable thing to key notes to -- its key is a hash of a path
	/// that may well change -- so what is learned there is about the machine rather than about it.
	/// </summary>
	[Test]
	public void A_directory_outside_git_files_machine_notes_under_the_user_folder()
	{
		using var fixture = GitFixture.Create();

		var stores = NoteStores.For(fixture.Plain("Scratch"), Options(fixture));

		stores.Machine.IsAvailable.ShouldBeTrue();
		Path.GetFileName(stores.Machine.Path).ShouldBe(NoteStores.UserFolder);
	}

	/// <summary>Every worktree writes to one machine store, which is the point of keying on the repository.</summary>
	[Test]
	public void Every_worktree_writes_to_one_machine_store()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");
		var worktree = fixture.LinkedWorktree(main, "Review");

		var fromMain = NoteStores.For(main, Options(fixture));
		var fromWorktree = NoteStores.For(worktree, Options(fixture));

		fromWorktree.Machine.Path.ShouldBe(fromMain.Machine.Path);
	}

	/// <summary>
	/// The other half, and the one the two stores exist to separate. A committed note is a tracked
	/// file: it belongs to the branch that learned the fact and is reviewed with the change it
	/// describes. Writing it into the main checkout puts it on whatever branch that has out and
	/// dirties a tree nobody in the session is looking at.
	/// </summary>
	[Test]
	public void A_worktree_commits_its_notes_to_its_own_checkout()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");
		var worktree = fixture.LinkedWorktree(main, "Review");

		GitFixture.Write(main, ".dotnotes/dotnotes.json", """{ "repository": "loom" }""");
		GitFixture.Write(worktree, ".dotnotes/dotnotes.json", """{ "repository": "loom" }""");

		var stores = NoteStores.For(worktree, Options(fixture));

		stores.Repo.Path.ShouldBe(Path.Combine(worktree, ".dotnotes", "notes"));
		stores.Repo.Path.ShouldNotBe(NoteStores.For(main, Options(fixture)).Repo.Path);
	}

	/// <summary>
	/// Adding the opt-in is itself a commit, and a commit happens on a branch. Gating on the main
	/// checkout means creating the file the refusal just named does nothing until it merges, which
	/// leaves the person no reason left to doubt they did it right.
	/// </summary>
	[Test]
	public void A_worktree_opts_in_on_its_own_branch()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");
		var worktree = fixture.LinkedWorktree(main, "Review");

		NoteStores.For(worktree, Options(fixture)).Repo.IsAvailable.ShouldBeFalse();

		GitFixture.Write(worktree, ".dotnotes/dotnotes.json", """{ "repository": "loom" }""");

		NoteStores.For(worktree, Options(fixture)).Repo.IsAvailable.ShouldBeTrue();
		NoteStores.For(main, Options(fixture)).Repo.IsAvailable.ShouldBeFalse();
	}

	/// <summary>
	/// The refusal has to name the checkout the caller is in, because that is the one they can opt
	/// in. Naming the main checkout sends them to opt in another branch.
	/// </summary>
	[Test]
	public void The_refusal_names_a_file_in_the_checkout_that_asked()
	{
		using var fixture = GitFixture.Create();
		var worktree = fixture.LinkedWorktree(fixture.Checkout("Loom"), "Review");

		var stores = NoteStores.For(worktree, Options(fixture));

		stores.Repo.Unavailable!.ShouldContain($"--init \"{worktree}\"");
	}

	/// <summary>
	/// Repository scope is off until the repository opts in, so a server registered once and used
	/// everywhere never drops an untracked folder into somebody else's repository.
	/// </summary>
	[Test]
	public void Repository_scope_is_refused_until_the_repository_opts_in()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var stores = NoteStores.For(checkout, Options(fixture));

		stores.Repo.IsAvailable.ShouldBeFalse();
		stores.Repo.Unavailable!.ShouldContain("--init");
		stores.Machine.IsAvailable.ShouldBeTrue();
	}

	/// <summary>
	/// The refusal names the file to create, so creating it has to be enough on its own. Holding the
	/// resolved identity between calls made following the tool's own instruction appear to do
	/// nothing -- which is worse than a refusal that explains nothing, because the person has no
	/// reason left to doubt they did it right.
	/// </summary>
	[Test]
	public void Committing_the_config_turns_repository_scope_on_without_a_restart()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		NoteStores.For(checkout, Options(fixture)).Repo.IsAvailable.ShouldBeFalse();

		GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "rosemcp" }""");

		NoteStores.For(checkout, Options(fixture)).Repo.IsAvailable.ShouldBeTrue();
	}

	[Test]
	public void Committing_the_config_turns_repository_scope_on()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "rosemcp" }""");

		var stores = NoteStores.For(checkout, Options(fixture));

		stores.Repo.IsAvailable.ShouldBeTrue();
		stores.Repo.Path.ShouldBe(Path.Combine(checkout, ".dotnotes", "notes"));
	}

	/// <summary>
	/// A repository that wants its notes somewhere else says so once, in the committed file, and
	/// every clone agrees. Nothing overrides it per machine, because a repository store at a path
	/// that differs per machine is not a repository store.
	/// </summary>
	[Test]
	public void The_committed_config_chooses_where_repository_notes_live()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "r", "notes": "../docs/notes" }""");

		var stores = NoteStores.For(checkout, Options(fixture));

		stores.Repo.Path.ShouldBe(Path.Combine(checkout, "docs", "notes"));
	}

	[Test]
	public void A_bare_repository_has_nothing_to_commit_a_note_to()
	{
		using var fixture = GitFixture.Create();

		var stores = NoteStores.For(fixture.Bare("mirror.git"), Options(fixture));

		stores.Repo.IsAvailable.ShouldBeFalse();
		stores.Repo.Unavailable!.ShouldContain("bare");
		stores.Machine.IsAvailable.ShouldBeTrue();
	}

	/// <summary>The precedence, one layer at a time, narrowest intent first.</summary>
	[Test]
	public void The_machine_store_comes_from_the_narrowest_layer_that_named_one()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		GitFixture.Write(
			MachineSettingsFile.DirectoryFor(local),
			"settings.json",
			$$"""{ "machineStore": {{System.Text.Json.JsonSerializer.Serialize(fixture.Plain("fromSettings"))}} }""");

		var options = new NoteOptions { LocalAppData = local };

		NoteStores.For(checkout, options).MachineRootSource.ShouldBe(MachineStoreSource.Settings);

		// Through the seam rather than a real variable: the process environment is shared by every
		// test running beside this one, and setting it here redirected all of them into one store.
		options.Environment = name =>
			name == NoteStores.StoreVariable ? fixture.Plain("fromEnvironment") : null;

		NoteStores.For(checkout, options).MachineRootSource.ShouldBe(MachineStoreSource.Environment);

		options.MachineStore = fixture.Plain("fromArgument");
		NoteStores.For(checkout, options).MachineRootSource.ShouldBe(MachineStoreSource.Argument);
	}

	[Test]
	public void With_nothing_configured_the_store_is_the_default_and_is_created_on_demand()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var stores = NoteStores.For(checkout, Options(fixture));

		stores.MachineRootSource.ShouldBe(MachineStoreSource.Default);
		stores.Machine.IsAvailable.ShouldBeTrue();
		Directory.Exists(stores.Machine.Path).ShouldBeFalse();

		stores.Machine.Ensure();

		Directory.Exists(stores.Machine.Path).ShouldBeTrue();
	}

	/// <summary>
	/// The vault on a drive that is not mounted. Falling back to the default would write notes to a
	/// second place nobody is looking at, which is the fragmentation this server removes arriving by
	/// a different door -- so a store somebody chose and that is not there refuses, naming it.
	/// </summary>
	[Test]
	public void A_chosen_store_that_cannot_be_reached_refuses_rather_than_falling_back()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var missing = Path.Combine(fixture.Root, "no", "such", "vault");

		var stores = NoteStores.For(checkout, Options(fixture, missing));

		stores.Machine.IsAvailable.ShouldBeFalse();
		stores.Machine.Unavailable!.ShouldContain(missing);
		Should.Throw<InvalidOperationException>(() => stores.Machine.Ensure());
	}

	/// <summary>Resolution is what a diagnostic does, and a diagnostic must not leave directories behind.</summary>
	[Test]
	public void Asking_where_the_stores_are_creates_nothing()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var stores = NoteStores.For(checkout, Options(fixture));

		Directory.Exists(stores.MachineRoot).ShouldBeFalse();
		Directory.Exists(stores.Machine.Path).ShouldBeFalse();
		Directory.Exists(Path.Combine(checkout, ".dotnotes")).ShouldBeFalse();
	}

	/// <summary>
	/// Settings that are there and broken say where notes go, so defaulting past a typo would put
	/// them somewhere the person is not looking and say nothing about it.
	/// </summary>
	[Test]
	public void Malformed_settings_refuse_and_name_the_file()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		var settings = GitFixture.Write(MachineSettingsFile.DirectoryFor(local), "settings.json", "{ not json");

		var refusal = Should.Throw<DotNotesConfigurationException>(
			() => NoteStores.For(checkout, new NoteOptions { LocalAppData = local }));

		refusal.Path.ShouldBe(settings);
	}

	/// <summary>A missing settings file is entirely normal and means the defaults.</summary>
	[Test]
	public void Missing_settings_are_the_defaults()
	{
		using var fixture = GitFixture.Create();

		var settings = MachineSettingsFile.Read(GitFixture.Under(fixture.Root, "localappdata"));

		settings.MachineStore.ShouldBeNull();
		settings.MachineName.ShouldBeNull();
	}
}
