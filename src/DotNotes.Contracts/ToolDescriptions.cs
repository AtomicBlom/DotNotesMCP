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
	/// The diagnostic. It names the worktree and the repository separately because the two stores
	/// answer to different ones, and seeing both is how a caller confirms which store an answer came
	/// from before concluding a note is missing.
	/// </summary>
	public const string Context =
		"Which repository this directory resolved to and how, where both stores are, and whether each "
			+ "can be written to. The private store is keyed to the repository and the committed one "
			+ "to this worktree; this says which, and what to fix when a store refuses. Not needed "
			+ "before other calls -- reach for it when an answer looks wrong.";

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

	/// <summary>
	/// The promotion warning earns its place. Renaming is ordinary, but moving a note to repository
	/// scope publishes it to everyone who clones, and the two arrive through the same tool -- so the
	/// asymmetry has to be said where the choice is made rather than left to be discovered.
	/// </summary>
	public const string Move =
		"Rename a note, or move it between stores, rewriting every link that pointed at it -- which "
			+ "is why this beats deleting and writing it again. Moving to repository scope publishes "
			+ "a private note to everyone who clones, and that cannot be undone by moving it back.";

	public const string Check =
		"Find what nothing else does: links that point at nothing, notes too long to finish, files a "
			+ "sync service copied, two notes claiming one name, frontmatter that no longer parses. "
			+ "Worth running after editing a store by hand.";

	public const string ScopeArgument =
		"Which store: machine, repository, or both. Defaults to both.";

	public const string ToNameArgument = "A new name, or omit to keep the current one.";

	public const string ToScopeArgument =
		"A new store: machine or repository. Omit to keep the current one. Moving to repository "
			+ "publishes the note to everyone who clones.";

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

	// The indexing mode. Its surface is never served beside the note tools, so these are budgeted
	// separately: an indexing session reads nothing else and has room to be told the shape properly.

	public const string IndexNext =
		"Claims the next note needing enrichment and returns everything needed to enrich it: the "
			+ "note, the topics already in use, accepted examples from this store, and the notes it "
			+ "could link to. Everything to read is in the result -- going looking makes the output "
			+ "depend on what you happened to find, and consistency across hundreds of notes is the "
			+ "whole value. Answers drained when nothing is left, so the loop ends by itself.";

	public const string IndexWrite =
		"Submits one note's enrichment and releases the claim. Writes dn- keys into the note's own "
			+ "frontmatter and touches nothing else, so the result is visible in Obsidian and "
			+ "outlives the index. Refuses and says what to fix when the shape is wrong or a topic "
			+ "was used without being declared -- a refusal is a correction, not an error.";

	public const string IndexSkip =
		"Gives a claim back. release puts the note back in the queue, not-worth-indexing answers it "
			+ "permanently for this version of the note, and unreadable counts a failure toward the "
			+ "retry limit.";

	public const string IndexStatus =
		"How much is left, what the vocabulary looks like, and who holds what. Ask for drift to see "
			+ "how recent enrichments compare to the store's own norms.";

	public const string IndexRebuild =
		"Puts notes back in the queue. Discards nothing on disk until each is re-enriched, but it "
			+ "does commit an agent to redoing them.";

	public const string LeaseArgument = "The lease from note_index_next.";

	public const string GistArgument =
		"One sentence under 140 characters, naming the subject and what the note says about it. Not "
			+ "\"This note...\" -- start with the subject. It is all a reader sees when deciding "
			+ "whether to open it.";

	public const string AsksArgument =
		"Three to seven questions this note answers, in the words somebody who has not read it would "
			+ "use. Search matches these directly, so they decide whether the note is findable at "
			+ "all. Name the specific type, error or file in at least one.";

	public const string TopicsArgument =
		"Two to six topics, preferring ones already in the supplied vocabulary. A topic used once is "
			+ "a topic nobody can filter by.";

	public const string NewTopicsArgument =
		"Any topic not already in the vocabulary. A topic used without being listed here is refused, "
			+ "which is what keeps the vocabulary from growing by accident.";

	public const string EntitiesArgument =
		"Proper nouns a search would type verbatim: type names, products, people, error codes. "
			+ "Copied exactly as the note spells them.";

	public const string AliasesArgument =
		"Other names for the subject, including an acronym or its expansion. Not the title again.";

	public const string LinksArgument =
		"Notes this one is genuinely about the same thing as, chosen only from the candidates "
			+ "supplied. Zero is a normal answer; a link to everything vaguely related makes the "
			+ "graph useless.";

	public const string ConfidenceArgument =
		"high, medium or low: about the note itself, not about your summary of it.";

	public const string DispositionArgument =
		"release, not-worth-indexing, or unreadable. No default -- the three differ in what they cost.";

	public const string SkipReasonArgument = "Why, in a few words.";

	public const string DriftArgument = "Also report how recent enrichments compare to the store's norms.";

	public const string SelectionArgument =
		"Which notes: stale, failed, skipped, or all. No default -- all commits to re-enriching the "
			+ "whole store.";

	public const string IndexScopeArgument =
		"Which store this run covers: machine or repository.";
}
