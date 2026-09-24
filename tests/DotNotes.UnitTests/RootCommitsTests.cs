using DotNotes.Notes.Repositories;

namespace DotNotes.UnitTests;

/// <summary>
/// That a repository's root commits are read from disk as git wrote them. They are the evidence that
/// recognises a repository after it has been moved, so a reader that misses one is a renamed
/// repository whose notes are never found again.
/// </summary>
public sealed class RootCommitsTests
{
	[Test]
	public void A_commit_graph_lists_the_commits_with_no_parent()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");
		var root = GitFixture.Commit(1);

		GitFixture.CommitGraph(checkout, root);

		RootCommits.InGraph(Path.Combine(checkout, ".git")).ShouldBe([root]);
	}

	/// <summary>A monorepo assembled from three repositories has three roots, and every one of them counts.</summary>
	[Test]
	public void Every_root_of_a_merged_history_is_found()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Platform");
		string[] roots = [GitFixture.Commit(1), GitFixture.Commit(2), GitFixture.Commit(3)];

		GitFixture.CommitGraph(checkout, roots);

		RootCommits.InGraph(Path.Combine(checkout, ".git")).Order().ShouldBe(roots);
	}

	[Test]
	public void A_sha256_graph_is_read_with_its_own_hash_length()
	{
		using var fixture = GitFixture.Create();
		var file = Path.Combine(fixture.Root, "commit-graph");
		var root = new string('b', 64);

		CommitGraphFile.Write(file, [root], hashLength: 32);

		RootCommits.FromGraph(file).ShouldBe([root]);
	}

	/// <summary>git splits the graph into layers as a repository grows; the roots may be in any of them.</summary>
	[Test]
	public void A_split_graph_is_read_layer_by_layer()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");
		var graphs = Path.Combine(checkout, ".git", "objects", "info", "commit-graphs");
		var first = new string('1', 40);
		var second = new string('2', 40);

		CommitGraphFile.Write(Path.Combine(graphs, $"graph-{first}.graph"), [GitFixture.Commit(1)]);
		CommitGraphFile.Write(Path.Combine(graphs, $"graph-{second}.graph"), [GitFixture.Commit(2)]);
		File.WriteAllText(Path.Combine(graphs, "commit-graph-chain"), $"{first}\n{second}\n");

		RootCommits.InGraph(Path.Combine(checkout, ".git")).Order().ShouldBe([GitFixture.Commit(1), GitFixture.Commit(2)]);
	}

	/// <summary>Evidence, not identity: a file that is not a graph is no roots, never a failure.</summary>
	[Test]
	public void A_file_that_is_not_a_graph_is_no_evidence()
	{
		using var fixture = GitFixture.Create();
		var file = GitFixture.Write(fixture.Root, "commit-graph", "not a graph at all");

		RootCommits.FromGraph(file).ShouldBeEmpty();
	}

	[Test]
	public void A_repository_without_a_graph_has_no_graph_roots()
	{
		using var fixture = GitFixture.Create();
		var checkout = fixture.Checkout("Widget");

		RootCommits.InGraph(Path.Combine(checkout, ".git")).ShouldBeEmpty();
		RootCommits.GraphStamp(Path.Combine(checkout, ".git")).ShouldBeEmpty();
	}

	/// <summary>
	/// A repository made with git init and not yet collected has no graph, and its reflog starts with
	/// the one commit that names it.
	/// </summary>
	[Test]
	public void The_reflog_names_the_initial_commit_of_a_repository_made_here()
	{
		var line = $"{new string('0', 40)} {GitFixture.Commit(7)} Steven Blom <s@example.com> 1789873432 +0930\tcommit (initial): Start";

		RootCommits.InitialFrom(line).ShouldBe(GitFixture.Commit(7));
	}

	/// <summary>A clone's reflog starts at the tip it fetched, which is not a root and must not be taken for one.</summary>
	[Test]
	public void A_clone_reflog_names_no_root()
	{
		var line = $"{new string('0', 40)} {GitFixture.Commit(7)} Steven Blom <s@example.com> 1789873432 +0930\tclone: from https://github.com/AtomicBlom/Widget.git";

		RootCommits.InitialFrom(line).ShouldBeNull();
	}
}
