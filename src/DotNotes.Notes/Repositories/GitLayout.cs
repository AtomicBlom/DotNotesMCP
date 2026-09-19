using DotNotes.Contracts;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// Where git keeps its state for a directory, read from disk without running git.
/// <para>
/// The step that matters is <c>commondir</c>. Finding <c>.git</c> answers "which checkout is this",
/// and six worktrees of one repository give six different answers -- which is the fragmentation
/// this server exists to remove. Following <c>commondir</c> answers "which repository is this", and
/// all six give one.
/// </para>
/// <para>
/// Reading rather than running is the same trade as reading a solution file without MSBuild. Every
/// call needs an identity, a process launch costs tens of milliseconds on Windows and more behind a
/// virus scanner, <c>git</c> need not be on the PATH of a server an editor launched, and a child
/// process would put the most important test in this repository behind one.
/// </para>
/// </summary>
public sealed record GitLayout
{
	private const string GitFileMarker = "gitdir:";

	private GitLayout()
	{
	}

	/// <summary>The directory holding HEAD and the index for this checkout.</summary>
	public required string GitDirectory { get; init; }

	/// <summary>
	/// What <c>git rev-parse --git-common-dir</c> answers: the git directory shared by every
	/// worktree of this repository. The same for all of them, which is the point.
	/// </summary>
	public required string CommonDirectory { get; init; }

	/// <summary>The working tree this call is inside -- a linked worktree's own root, not the repository's.</summary>
	public required string Worktree { get; init; }

	/// <summary>
	/// The one working tree notes are keyed to: the main checkout, whichever worktree asked. Null
	/// where there is none to derive, which is a bare repository or a common directory that is not
	/// named <c>.git</c> and so implies no tree beside it.
	/// </summary>
	public string? Root { get; init; }

	public required RepositoryKind Kind { get; init; }

	/// <summary>
	/// The layout covering a directory, or null when no git directory is above it.
	/// <para>
	/// Three shapes, separated by two cheap facts. A <c>.git</c> that is a directory is an ordinary
	/// checkout. A <c>.git</c> that is a file names a git directory elsewhere, and whether that
	/// directory carries a <c>commondir</c> is the whole difference between a linked worktree, whose
	/// notes belong to the main checkout, and a submodule, which is its own repository with its own
	/// remote and its own notes.
	/// </para>
	/// </summary>
	public static GitLayout? Find(string startDirectory)
	{
		var start = CanonicalPath.Of(startDirectory);
		if (start.Length == 0) return null;

		var directory = new DirectoryInfo(start);

		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName, ".git");

			if (Directory.Exists(candidate)) return Resolve(candidate, directory.FullName, linked: false);

			if (File.Exists(candidate) && Linked(candidate, directory.FullName) is { } linked)
			{
				return Resolve(linked, directory.FullName, linked: true);
			}

			directory = directory.Parent;
		}

		return Bare(start);
	}

	/// <summary>
	/// The layout implied by a git directory, once it is known whether a <c>.git</c> file pointed at
	/// it.
	/// </summary>
	private static GitLayout Resolve(string gitDirectory, string worktree, bool linked)
	{
		var common = CanonicalPath.Of(gitDirectory);
		var kind = linked ? RepositoryKind.Submodule : RepositoryKind.Checkout;

		if (Read(Path.Combine(gitDirectory, "commondir")) is { Length: > 0 } relative)
		{
			common = CanonicalPath.Of(Path.Combine(gitDirectory, relative.Trim()));
			kind = RepositoryKind.LinkedWorktree;
		}

		return new GitLayout
		{
			GitDirectory = CanonicalPath.Of(gitDirectory),
			CommonDirectory = common,
			Worktree = CanonicalPath.Of(worktree),

			// A submodule's working tree is its own, and a linked worktree's repository lives beside
			// the common directory. Anywhere else -- a repository moved aside with
			// --separate-git-dir -- there is no tree to infer, and saying so beats naming a
			// directory that holds somebody else's files.
			Root = kind switch
			{
				RepositoryKind.Submodule => CanonicalPath.Of(worktree),
				_ => BesideGitDirectory(common),
			},
			Kind = kind,
		};
	}

	/// <summary>
	/// A repository with no working tree at all, which is a directory that holds what a git directory
	/// holds. Checked only once the walk has found no <c>.git</c>, so an ordinary checkout never pays
	/// for it.
	/// </summary>
	private static GitLayout? Bare(string directory)
	{
		var head = File.Exists(Path.Combine(directory, "HEAD"));
		var objects = Directory.Exists(Path.Combine(directory, "objects"));
		var refs = Directory.Exists(Path.Combine(directory, "refs"));

		if (!head || !objects || !refs) return null;

		return new GitLayout
		{
			GitDirectory = directory,
			CommonDirectory = directory,
			Worktree = directory,
			Root = null,
			Kind = RepositoryKind.Bare,
		};
	}

	/// <summary>The directory a <c>.git</c> named <paramref name="common"/> sits in, or null for anything else.</summary>
	private static string? BesideGitDirectory(string common) =>
		string.Equals(Path.GetFileName(common), ".git", StringComparison.OrdinalIgnoreCase)
			? Path.GetDirectoryName(common)
			: null;

	/// <summary>
	/// The directory a <c>.git</c> file points at, or null for anything that is not one. The path it
	/// names is usually absolute and is allowed to be relative to the worktree.
	/// </summary>
	private static string? Linked(string gitFile, string worktree)
	{
		if (Read(gitFile) is not { } text) return null;

		foreach (var line in text.Split('\n'))
		{
			var trimmed = line.Trim();
			if (!trimmed.StartsWith(GitFileMarker, StringComparison.Ordinal)) continue;

			var target = trimmed[GitFileMarker.Length..].Trim();
			if (target.Length == 0) return null;

			var resolved = Path.GetFullPath(Path.Combine(worktree, target));

			return Directory.Exists(resolved) ? resolved : null;
		}

		return null;
	}

	/// <summary>
	/// A small git file's contents, or null where it is absent or unreadable. Absent is an answer
	/// here -- no <c>commondir</c> is what makes a submodule a submodule -- so a missing file must
	/// not be a failure.
	/// </summary>
	private static string? Read(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
