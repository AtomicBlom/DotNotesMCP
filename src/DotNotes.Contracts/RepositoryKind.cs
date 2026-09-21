namespace DotNotes.Contracts;

/// <summary>
/// What kind of git checkout a directory sits in, which decides whether there is a working tree to
/// commit a note to.
/// </summary>
public enum RepositoryKind
{
	/// <summary>No <c>.git</c> above this directory. Machine scope only.</summary>
	NoRepository,

	/// <summary>An ordinary clone: <c>.git</c> is a directory.</summary>
	Checkout,

	/// <summary>
	/// A linked worktree: <c>.git</c> is a file naming a directory under the main repository's
	/// <c>worktrees</c> folder, which carries a <c>commondir</c>. It shares the main checkout's
	/// private notes and commits its own.
	/// </summary>
	LinkedWorktree,

	/// <summary>
	/// A submodule: <c>.git</c> is a file naming a directory under the superproject's
	/// <c>modules</c> folder, which carries no <c>commondir</c>. It is its own repository with its
	/// own remote, and its notes are its own.
	/// </summary>
	Submodule,

	/// <summary>A repository with no working tree, so nothing to commit a note to.</summary>
	Bare,
}
