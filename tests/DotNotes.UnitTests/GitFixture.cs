namespace DotNotes.UnitTests;

/// <summary>
/// A git layout staged on disk, without git.
/// <para>
/// The code under test reads <c>.git</c>, <c>commondir</c> and <c>config</c> as files, so the
/// fixture writes those files and nothing else. Running real git would make the most important
/// table in this repository depend on git being installed and on a child process per row.
/// </para>
/// </summary>
public sealed class GitFixture : IDisposable
{
	private GitFixture(string root) => Root = root;

	/// <summary>The temporary directory everything in this fixture lives under.</summary>
	public string Root { get; }

	public static GitFixture Create() =>
		new(Directory.CreateDirectory(
			Path.Combine(Path.GetTempPath(), "dotnotes-tests", Guid.NewGuid().ToString("n"))).FullName);

	/// <summary>
	/// An ordinary clone: a real <c>.git</c> directory, with an origin named after the checkout
	/// unless one is given. Derived rather than fixed, so a test that stages two repositories gets
	/// two remotes without saying so -- one shared default would silently make them one repository,
	/// which is the condition half of these tests exist to detect.
	/// </summary>
	/// <param name="name">The directory to create under the fixture root.</param>
	/// <param name="remote">The origin URL, or null for a clone that has none.</param>
	public string Checkout(string name, string? remote = null)
	{
		var worktree = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
		var git = Directory.CreateDirectory(Path.Combine(worktree, ".git")).FullName;

		Populate(git, remote ?? $"https://github.com/AtomicBlom/{Path.GetFileName(name)}.git");

		return worktree;
	}

	/// <summary>An ordinary clone with no origin remote at all, so naming falls to the folder.</summary>
	public string CheckoutWithoutRemote(string name)
	{
		var worktree = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

		Populate(Directory.CreateDirectory(Path.Combine(worktree, ".git")).FullName, remote: null);

		return worktree;
	}

	/// <summary>
	/// A linked worktree of <paramref name="mainCheckout"/>: a <c>.git</c> file, and a git directory
	/// under the main repository's <c>worktrees</c> folder carrying a <c>commondir</c>.
	/// </summary>
	/// <param name="mainCheckout">The repository this is a worktree of.</param>
	/// <param name="name">The worktree's directory, and its name under <c>worktrees</c>.</param>
	/// <param name="relative">
	/// Whether the <c>.git</c> file names its target relatively. Git writes an absolute path, and
	/// accepts a relative one; a reader that resolves it against the process working directory rather
	/// than the worktree finds nothing, and reports every worktree as its own repository.
	/// </param>
	public string LinkedWorktree(string mainCheckout, string name, bool relative = false)
	{
		var worktree = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
		var mainGit = Path.Combine(mainCheckout, ".git");
		var linked = Directory.CreateDirectory(Path.Combine(mainGit, "worktrees", name)).FullName;

		File.WriteAllText(Path.Combine(linked, "commondir"), "../..\n");
		File.WriteAllText(Path.Combine(linked, "HEAD"), "ref: refs/heads/main\n");

		var target = relative ? Path.GetRelativePath(worktree, linked) : linked;
		File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {target.Replace('\\', '/')}\n");

		return worktree;
	}

	/// <summary>
	/// A submodule: a <c>.git</c> file pointing under the superproject's <c>modules</c> folder, with
	/// no <c>commondir</c> beside it. That one absent file is the whole difference from a worktree.
	/// </summary>
	public string Submodule(string superproject, string name, string? remote)
	{
		var worktree = Directory.CreateDirectory(Path.Combine(superproject, name)).FullName;
		var git = Directory.CreateDirectory(
			Path.Combine(superproject, ".git", "modules", name)).FullName;

		Populate(git, remote);
		File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {git.Replace('\\', '/')}\n");

		return worktree;
	}

	/// <summary>A repository with no working tree: what a git directory holds, and nothing else.</summary>
	public string Bare(string name)
	{
		var bare = Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

		Populate(bare, remote: null);

		return bare;
	}

	/// <summary>
	/// Gives a checkout an origin, or changes it -- the ordinary act that renames a repository which
	/// was named by its folder.
	/// </summary>
	public static void SetRemote(string checkout, string remote) =>
		Populate(Path.Combine(checkout, ".git"), remote);

	/// <summary>
	/// Writes a commit-graph listing these roots, each with one child, so the reader has to tell the
	/// two apart. The hashes are whatever the test says they are: nothing here checks them.
	/// </summary>
	public static void CommitGraph(string checkout, params string[] roots) =>
		CommitGraphFile.Write(
			Path.Combine(checkout, ".git", "objects", "info", "commit-graph"),
			roots);

	/// <summary>A distinct forty-character commit id, from a readable seed.</summary>
	public static string Commit(int seed) => seed.ToString("x8").PadLeft(40, 'a');

	/// <summary>A directory with no git anywhere above it.</summary>
	public string Plain(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

	/// <summary>A subdirectory, for asking the same question from further down a tree.</summary>
	public static string Under(string directory, params string[] segments) =>
		Directory.CreateDirectory(Path.Combine([directory, .. segments])).FullName;

	/// <summary>
	/// Writes a file into a directory, creating it if need be, and answers the path the production
	/// code would build. Separators are normalised: a test that writes ".dotnotes/dotnotes.json" and
	/// compares against a Path.Combine of the same two segments otherwise fails on the slash alone.
	/// </summary>
	public static string Write(string directory, string relativePath, string content)
	{
		var path = Path.Combine([directory, .. relativePath.Split('/', '\\')]);

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);

		return path;
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A leftover temp directory is not worth failing a passing test over.
		}
	}

	/// <summary>The three things that make a directory look like a git directory, plus a remote.</summary>
	private static void Populate(string gitDirectory, string? remote)
	{
		Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
		Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
		File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");

		var config = remote is null
			? "[core]\n\tbare = false\n"
			: $"[core]\n\tbare = false\n[remote \"origin\"]\n\turl = {remote}\n\tfetch = +refs/heads/*\n";

		File.WriteAllText(Path.Combine(gitDirectory, "config"), config);
	}
}
