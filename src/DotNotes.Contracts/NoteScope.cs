namespace DotNotes.Contracts;

/// <summary>
/// Which store a note lives in. The choice is stated by the caller and never inferred, because it
/// is the one decision here with no undo: a private note can be promoted, and a pushed one cannot
/// be recalled.
/// </summary>
public enum NoteScope
{
	/// <summary>
	/// Private to this note store, never committed. Works in every repository with no setup, which
	/// is what lets the server be registered once and used everywhere.
	/// </summary>
	Machine,

	/// <summary>
	/// Committed with the code and read by everyone who clones it. Available only where the
	/// repository has opted in by committing <c>.dotnotes/dotnotes.json</c>.
	/// </summary>
	Repository,
}
