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

	public const string Write =
		"Record something worth having next session, or replace a note that has gone stale. Cheaper "
			+ "than restating it next time and than the user repeating themselves. A note is whole: "
			+ "there is no append, so a note that has outgrown itself gets split and linked as "
			+ "[[name]], or across stores as [[machine:name]] and [[repo:name]].";

	public const string Delete =
		"Remove a note that is wrong or spent. It reports what now links to nothing, so a retraction "
			+ "does not quietly leave the notes that referenced it pointing at a gap.";

	public const string ScopeArgument =
		"Which store: machine, repository, or both. Defaults to both.";

	/// <summary>
	/// The one argument that earns its length. This is the decision with no undo, and it is read
	/// here rather than in the instructions because here is where it is being made -- the
	/// instructions are loaded into every session, including the many that never write a note.
	/// </summary>
	public const string WriteScopeArgument =
		"repository = committed with the code, read by everyone who clones it. machine = private, "
			+ "never committed: local paths, machine quirks, anything naming a person or customer. "
			+ "No default: a private note can be promoted, a pushed one cannot.";

	public const string NoteNameArgument =
		"A short slug naming the subject, reused to replace the note later and to link to it.";

	public const string DescriptionArgument =
		"One line saying what this note says, not what it is about. It is what a search shows.";

	public const string BodyArgument =
		"The note itself, in markdown. Link related notes as [[name]].";

	public const string NoteTypeArgument =
		"project (about the work), user (about the person), feedback (how to work), or reference "
			+ "(a pointer outward). Defaults to project.";

	public const string NoteTagsArgument = "A few tags to find this note by later.";

	public const string MachinesArgument =
		"Machines this is true of, where it is not true of all of them. A note naming one is still "
			+ "shown on the others, flagged.";

	public const string RevisionArgument =
		"The revision a read reported. Required when replacing a note, and refused if it has changed "
			+ "since -- the user edits these files in Obsidian while a session is running.";

	public const string TypeArgument =
		"Only notes of one kind: project, user, feedback or reference.";

	public const string TagsArgument = "Only notes carrying all of these tags.";

	public const string LimitArgument = "How many hits. Ten by default, fifty at most.";

	public const string NameArgument = "The note's name, as a search hit reports it.";

	public const string DirectoryArgument =
		"Which directory to resolve. Defaults to where the session is working.";
}
