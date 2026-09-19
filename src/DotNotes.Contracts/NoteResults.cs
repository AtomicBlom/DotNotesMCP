namespace DotNotes.Contracts;

/// <summary>
/// What every result carries: which repository answered, and which stores were read.
/// <para>
/// A search that found nothing in the wrong store is shaped exactly like a search that found
/// nothing in the right one, which is what six per-worktree stores feel like from the inside.
/// Naming the repository on every answer is what makes the difference visible without the caller
/// having to suspect it first.
/// </para>
/// <para>
/// Two fields, not four. A store's absolute path on every hit is bytes the model cannot act on, so
/// it appears on <see cref="NoteContextResult"/> alone -- where somebody is asking precisely that.
/// </para>
/// </summary>
public abstract record NoteResult
{
	/// <summary>The repository key these notes are filed under.</summary>
	public string Repository { get; init; } = string.Empty;

	/// <summary>Which stores answered: machine, repository, or both.</summary>
	public string Scope { get; init; } = string.Empty;
}

/// <summary>Ranked hits, as summaries. Never whole notes -- that is what <c>note_read</c> is for.</summary>
public sealed record NoteSearchResult : NoteResult
{
	public required IReadOnlyList<NoteMatch> Matches { get; init; }

	/// <summary>How many notes were searched, so an empty answer distinguishes itself from an empty store.</summary>
	public required int Searched { get; init; }

	/// <summary>Whether there were more hits than the limit allowed.</summary>
	public required bool Truncated { get; init; }

	/// <summary>Which search implementation answered. The seam, reported rather than assumed.</summary>
	public required string Backend { get; init; }

	/// <summary>What the caller needs to know that no field says, such as a store that refused.</summary>
	public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>One hit.</summary>
public sealed record NoteMatch
{
	public required NoteHeading Note { get; init; }

	/// <summary>A window of the note's own text around what matched.</summary>
	public required string Extract { get; init; }

	public required double Score { get; init; }

	/// <summary>
	/// True where the note is about machines this is not one of. Shown rather than hidden, because
	/// the other machine's quirk is often exactly what is being looked for.
	/// </summary>
	public bool OtherMachine { get; init; }
}

/// <summary>One note, whole.</summary>
public sealed record NoteContent : NoteResult
{
	public required NoteHeading Note { get; init; }

	public required string Body { get; init; }

	/// <summary>Where this note points.</summary>
	public required IReadOnlyList<NoteLink> Links { get; init; }

	/// <summary>What points here, which is the half Obsidian shows and a file read does not.</summary>
	public required IReadOnlyList<NoteHeading> Backlinks { get; init; }

	public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>A link out of a note, and whether it goes anywhere.</summary>
public sealed record NoteLink
{
	public required string Target { get; init; }

	/// <summary>The store the link names, where it says.</summary>
	public string? Scope { get; init; }

	/// <summary>False where nothing of that name is in the stores searched.</summary>
	public required bool Resolved { get; init; }
}

/// <summary>Where this is, where the stores are, and what is wrong with either.</summary>
public sealed record NoteContextResult : NoteResult
{
	/// <summary>The directory that was resolved.</summary>
	public required string Directory { get; init; }

	/// <summary>Checkout, LinkedWorktree, Submodule, Bare, or NoRepository.</summary>
	public required string Kind { get; init; }

	/// <summary>The checkout this call is inside, which is not the repository when it is a worktree.</summary>
	public string? Worktree { get; init; }

	/// <summary>The one working tree the notes are keyed to.</summary>
	public string? Root { get; init; }

	/// <summary>The origin remote, folded so every spelling of it agrees.</summary>
	public string? Remote { get; init; }

	/// <summary>Which step of the chain named the repository.</summary>
	public required string NamedBy { get; init; }

	/// <summary>What this machine calls itself in a note's machines list.</summary>
	public required string Machine { get; init; }

	public required NoteStoreState MachineStore { get; init; }

	public required NoteStoreState RepositoryStore { get; init; }

	public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>One store: where, whether it can be written to, and how much is in it.</summary>
public sealed record NoteStoreState
{
	public required string Path { get; init; }

	public required bool Writable { get; init; }

	/// <summary>How many notes are in it, or zero where it cannot be read.</summary>
	public required int Notes { get; init; }

	/// <summary>Why it cannot be used, in a sentence naming the fix. Null when it can.</summary>
	public string? Unavailable { get; init; }
}
