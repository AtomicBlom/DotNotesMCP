namespace DotNotes.Contracts;

/// <summary>
/// What each tool says about itself.
/// <para>
/// An agent reaches for what it knows, so every description has to name the thing the caller would
/// otherwise have done -- reading the files, asking again, restating it next session. A tool that
/// works and still loses to reading files lost for a reason, and the reason is usually here.
/// </para>
/// <para>
/// The whole model-facing surface is paid for at the start of every session, before a single call,
/// so the target is the fewest words that produce the right behaviour. Reasoning goes in these doc
/// comments, which a maintainer reads and a model never does.
/// </para>
/// </summary>
public static class ToolDescriptions
{
	public const string Search =
		"Find notes about this repository and this machine, ranked, as one-line summaries rather than "
			+ "whole notes. Reach for it before reading files to reconstruct a decision, and before "
			+ "asking the user something they may already have answered: a note is what was learned "
			+ "last time, where the code only shows what was done. No query lists everything.";

	public const string Read =
		"One note whole, by name, with the notes it links to and the notes that link to it. Use it on "
			+ "a search hit worth the full text; search returns extracts precisely so reading the "
			+ "whole note is a choice rather than the cost of looking.";

	/// <summary>
	/// The diagnostic. It names the worktree and the repository separately because that difference
	/// is the server's whole premise, and seeing them is how a caller confirms a worktree is reading
	/// the repository's notes rather than its own.
	/// </summary>
	public const string Context =
		"Which repository this directory resolved to and how, where both stores are, and whether each "
			+ "can be written to. Notes are keyed to the repository rather than the worktree, so every "
			+ "checkout shares one set; this says which, and what to fix when a store refuses. Not "
			+ "needed before other calls -- reach for it when an answer looks wrong.";

	/// <summary>
	/// Naming the identifier behaviour is worth its characters. It is the one thing about this search
	/// a caller cannot guess, and not knowing it means phrasing queries as exact names.
	/// </summary>
	public const string QueryArgument =
		"Words to match. Identifiers match whole and by their parts, so \"bindable property\" finds a "
			+ "note that only wrote GeneratedBindableCustomProperty. Omit to list everything.";

	public const string ScopeArgument =
		"Which store: machine, repository, or both. Defaults to both.";

	public const string TypeArgument =
		"Only notes of one kind: project, user, feedback or reference.";

	public const string TagsArgument = "Only notes carrying all of these tags.";

	public const string LimitArgument = "How many hits. Ten by default, fifty at most.";

	public const string NameArgument = "The note's name, as a search hit reports it.";

	public const string DirectoryArgument =
		"Which directory to resolve. Defaults to where the session is working.";
}
