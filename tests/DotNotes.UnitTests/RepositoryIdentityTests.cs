using DotNotes.Contracts;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;

namespace DotNotes.UnitTests;

/// <summary>
/// That every checkout of one repository answers with one key, and that two repositories never
/// share one.
/// <para>
/// This is the table the server exists for. The memory it replaces keys on the working directory,
/// which gives a repository as many stores as it has worktrees -- six for one of them here -- and
/// gives two stores to one repository reached through a differently-cased drive letter. Every row
/// below is one of those failures, written as the answer it should have given.
/// </para>
/// </summary>
public sealed class RepositoryIdentityTests
{
	[Test]
	public void An_ordinary_clone_is_named_by_its_remote()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var identity = RepositoryIdentity.For(checkout);

		identity.Kind.ShouldBe(RepositoryKind.Checkout);
		identity.Root.ShouldBe(checkout);
		identity.Remote.ShouldBe("github.com/atomicblom/rosemcp");
		identity.Name.ShouldBe("rosemcp");
		identity.Key.ShouldBe("rosemcp");
		identity.NamedBy.ShouldBe(RepositoryNameSource.OriginRemote);
	}

	/// <summary>A call from deep in a tree is a call about the repository, not about the directory.</summary>
	[Test]
	public void A_subdirectory_answers_as_its_repository()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var deep = GitFixture.Under(checkout, "src", "RoseMcp.Broker", "Tools");

		RepositoryIdentity.For(deep).Key.ShouldBe(RepositoryIdentity.For(checkout).Key);
		RepositoryIdentity.For(deep).Root.ShouldBe(checkout);
	}

	/// <summary>
	/// The headline. Six worktrees of Loom hold six memory stores today, and the seventh starts
	/// empty; this is the assertion that says they are one repository.
	/// </summary>
	[Test]
	public void Every_worktree_of_a_repository_answers_with_one_key()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");

		string[] worktrees =
		[
			main,
			fixture.LinkedWorktree(main, "Review"),
			fixture.LinkedWorktree(main, "Milestone11"),
			fixture.LinkedWorktree(main, "Scaling"),
			fixture.LinkedWorktree(main, "Fail-Loud", relative: true),
			fixture.LinkedWorktree(main, "Milestone13"),
		];

		var keys = worktrees.Select(worktree => RepositoryIdentity.For(worktree).Key).Distinct().ToArray();

		keys.ShouldBe(["loom"]);
	}

	/// <summary>
	/// A worktree knows both where it is and which repository it belongs to, and the two differ.
	/// Reporting only one of them is how a caller cannot tell a shared store from its own.
	/// </summary>
	[Test]
	public void A_linked_worktree_names_itself_and_its_repository()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");
		var worktree = fixture.LinkedWorktree(main, "Review");

		var identity = RepositoryIdentity.For(worktree);

		identity.Kind.ShouldBe(RepositoryKind.LinkedWorktree);
		identity.Worktree.ShouldBe(worktree);
		identity.Root.ShouldBe(main);
		identity.CommonDirectory.ShouldBe(Path.Combine(main, ".git"));
	}

	/// <summary>
	/// Git writes an absolute gitdir and accepts a relative one. Resolving a relative target against
	/// the process working directory finds nothing, and every worktree becomes its own repository --
	/// silently, because falling through to the directory name still produces a plausible answer.
	/// </summary>
	[Test]
	public void A_relative_gitdir_resolves_against_the_worktree()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("Loom");
		var worktree = fixture.LinkedWorktree(main, "Relative", relative: true);

		var identity = RepositoryIdentity.For(worktree);

		identity.Kind.ShouldBe(RepositoryKind.LinkedWorktree);
		identity.Key.ShouldBe("loom");
	}

	/// <summary>
	/// A submodule has its own remote and its own history, so its notes are its own. It reaches its
	/// git directory through a .git file exactly as a worktree does, and only the absent commondir
	/// separates them.
	/// </summary>
	[Test]
	public void A_submodule_is_its_own_repository()
	{
		using var fixture = GitFixture.Create();
		var super = fixture.Checkout("Super", "https://github.com/AtomicBlom/Super.git");
		var submodule = fixture.Submodule(super, "vendored", "https://github.com/other/Vendored.git");

		var identity = RepositoryIdentity.For(submodule);

		identity.Kind.ShouldBe(RepositoryKind.Submodule);
		identity.Root.ShouldBe(submodule);
		identity.Key.ShouldBe("vendored");
		identity.Key.ShouldNotBe(RepositoryIdentity.For(super).Key);
	}

	/// <summary>A repository with no working tree has nowhere to commit a note to, and says so.</summary>
	[Test]
	public void A_bare_repository_has_no_working_tree()
	{
		using var fixture = GitFixture.Create();

		var identity = RepositoryIdentity.For(fixture.Bare("mirror.git"));

		identity.Kind.ShouldBe(RepositoryKind.Bare);
		identity.Root.ShouldBeNull();
		identity.HasWorkingTree.ShouldBeFalse();
	}

	/// <summary>
	/// Outside git there is still a name, because machine-scope notes are filed under one and a
	/// scratch directory is somewhere people work. There is no working tree to commit to.
	/// </summary>
	[Test]
	public void A_directory_outside_git_still_has_a_name()
	{
		using var fixture = GitFixture.Create();

		var identity = RepositoryIdentity.For(fixture.Plain("Scratch"));

		identity.Kind.ShouldBe(RepositoryKind.NoRepository);
		identity.HasWorkingTree.ShouldBeFalse();
		identity.Name.ShouldBe("scratch");
		identity.NamedBy.ShouldBe(RepositoryNameSource.DirectoryName);
	}

	/// <summary>
	/// The drive-letter bug, which is the whole reason paths are folded before they are keyed on.
	/// Claude Code's own store has both D--Contoso-Platform and d--Contoso-Platform for one repository.
	/// </summary>
	[Test]
	public void Paths_that_differ_only_in_case_are_one_repository()
	{
		if (!PathCasing.IsInsensitive) return;

		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		var upper = RepositoryIdentity.For(checkout.ToUpperInvariant());
		var lower = RepositoryIdentity.For(checkout.ToLowerInvariant());

		upper.Key.ShouldBe(lower.Key);
	}

	/// <summary>A trailing separator and a traversal name the same directory, and must key the same.</summary>
	[Test]
	public void A_trailing_separator_and_a_traversal_are_the_same_directory()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var expected = RepositoryIdentity.For(checkout).Key;

		RepositoryIdentity.For(checkout + Path.DirectorySeparatorChar).Key.ShouldBe(expected);
		RepositoryIdentity.For(Path.Combine(checkout, "src", "..")).Key.ShouldBe(expected);
	}

	/// <summary>
	/// A committed name beats the remote, which is the answer for a remote whose spellings will not
	/// fold together and for a repository that has been renamed.
	/// </summary>
	[Test]
	public void A_committed_name_beats_the_remote()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");

		GitFixture.Write(checkout, ".dotnotes/dotnotes.json", """{ "repository": "Rose MCP" }""");

		var identity = RepositoryIdentity.For(checkout);

		identity.Name.ShouldBe("rose-mcp");
		identity.NamedBy.ShouldBe(RepositoryNameSource.ConfiguredName);
		identity.Config.ShouldNotBeNull();
	}

	/// <summary>
	/// A committed file is a branch's file, so two checkouts can hold two spellings of the name at
	/// once -- mid-rename, or on a branch that adds the config. Only the main checkout is allowed to
	/// answer, because a name that varies by branch is a repository with two machine stores, which
	/// is the fragmentation this whole table exists to detect.
	/// </summary>
	[Test]
	public void A_worktree_cannot_rename_the_repository_out_from_under_the_machine_store()
	{
		using var fixture = GitFixture.Create();
		var main = fixture.Checkout("RoseMCP");
		var worktree = fixture.LinkedWorktree(main, "Rename");

		GitFixture.Write(main, ".dotnotes/dotnotes.json", """{ "repository": "rose-mcp" }""");
		GitFixture.Write(worktree, ".dotnotes/dotnotes.json", """{ "repository": "something-else" }""");

		var identity = RepositoryIdentity.For(worktree);

		identity.Key.ShouldBe(RepositoryIdentity.For(main).Key);
		identity.Name.ShouldBe("rose-mcp");

		// The checkout's own config still gates and locates its committed notes; it just does not name.
		identity.Config!.Repository.ShouldBe("something-else");
		identity.NamingConfig!.Repository.ShouldBe("rose-mcp");
	}

	/// <summary>
	/// The remote beats the folder name, which is what makes the x64 and ARM64 machines agree: one
	/// repository cloned to two paths, under two folder names, is one store.
	/// </summary>
	[Test]
	public void Two_clones_of_one_remote_share_one_key()
	{
		using var fixture = GitFixture.Create();
		const string Remote = "https://github.com/AtomicBlom/RoseMCP.git";

		var here = fixture.Checkout("RoseMCP", Remote);
		var there = fixture.Checkout("rose-mcp-again", Remote);

		RepositoryIdentity.For(there).Key.ShouldBe(RepositoryIdentity.For(here).Key);
		RepositoryIdentity.For(there).Key.ShouldBe("rosemcp");
	}

	/// <summary>And two repositories that are genuinely different never collapse into one.</summary>
	[Test]
	public void Two_repositories_keep_two_keys()
	{
		using var fixture = GitFixture.Create();

		var rose = RepositoryIdentity.For(fixture.Checkout("RoseMCP"));
		var loom = RepositoryIdentity.For(fixture.Checkout("Loom"));

		rose.Key.ShouldBe("rosemcp");
		loom.Key.ShouldBe("loom");
	}

	/// <summary>
	/// A folder name means something only on this machine, so two unrelated directories that share
	/// one must not share a store. The hash is what keeps them apart.
	/// </summary>
	[Test]
	public void Two_unrelated_directories_with_one_name_stay_apart()
	{
		using var fixture = GitFixture.Create();
		var first = fixture.CheckoutWithoutRemote(Path.Combine("a", "tools"));
		var second = fixture.CheckoutWithoutRemote(Path.Combine("b", "tools"));

		var one = RepositoryIdentity.For(first);
		var other = RepositoryIdentity.For(second);

		one.Name.ShouldBe("tools");
		other.Name.ShouldBe("tools");
		one.Key.ShouldNotBe(other.Key);
		one.Key.ShouldStartWith("tools-");
	}

	/// <summary>
	/// A repository that has not opted in still resolves, because machine-scope notes are filed
	/// under its key. Only repository scope is off.
	/// </summary>
	[Test]
	public void A_repository_without_a_config_still_resolves()
	{
		using var fixture = GitFixture.Create();

		var identity = RepositoryIdentity.For(fixture.Checkout("RoseMCP"));

		identity.Config.ShouldBeNull();
		identity.Key.ShouldBe("rosemcp");
	}

	/// <summary>
	/// A config that is there and broken decides which key is used, so defaulting past it would file
	/// a repository's notes under a second name without saying anything. It refuses, naming the file.
	/// </summary>
	[Test]
	public void A_malformed_config_refuses_and_names_the_file()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("RoseMCP");
		var config = GitFixture.Write(checkout, ".dotnotes/dotnotes.json", "{ not json");


		var refusal = Should.Throw<DotNotesConfigurationException>(() => RepositoryIdentity.For(checkout));

		refusal.Path.ShouldBe(config);
		refusal.Message.ShouldContain("dotnotes.json");
	}

	/// <summary>A sibling whose name merely starts with .git is not a git directory.</summary>
	[Test]
	public void A_gitignore_is_not_a_git_directory()
	{
		using var fixture = GitFixture.Create();
		var plain = fixture.Plain("Scratch");

		File.WriteAllText(Path.Combine(plain, ".gitignore"), "bin/\n");

		RepositoryIdentity.For(plain).Kind.ShouldBe(RepositoryKind.NoRepository);
	}

	/// <summary>
	/// Nothing here is remembered between calls, because a directory becoming a repository is one of
	/// the two things a person does while a session is already open. Asking, running <c>git init</c>,
	/// and asking again must give the new answer: a resolution cached on the first reply leaves the
	/// server insisting there is no repository until it is restarted, with nothing to suggest why.
	/// </summary>
	[Test]
	public void A_directory_that_becomes_a_repository_is_noticed()
	{
		using var fixture = GitFixture.Create();
		var scratch = fixture.Plain("Scratch");

		RepositoryIdentity.For(scratch).Kind.ShouldBe(RepositoryKind.NoRepository);

		fixture.Checkout("Scratch");

		var after = RepositoryIdentity.For(scratch);

		after.Kind.ShouldBe(RepositoryKind.Checkout);
		after.Name.ShouldBe("scratch");
	}
}
