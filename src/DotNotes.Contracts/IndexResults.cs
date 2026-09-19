namespace DotNotes.Contracts;

/// <summary>Why a note is in the queue. Carried on the assignment, because it changes what to write.</summary>
public enum StaleReason
{
	/// <summary>Never enriched. A hole is invisible to a search, so these go first.</summary>
	NeverIndexed,

	/// <summary>The note has been edited since, so its enrichment describes something else.</summary>
	SourceChanged,

	/// <summary>The enrichment's shape has changed and this one is in the old one.</summary>
	SchemaChanged,

	/// <summary>The instructions have changed, so this was written to a different brief.</summary>
	PromptChanged,

	/// <summary>An earlier attempt failed and is being tried again.</summary>
	Retry,

	/// <summary>Put back in the queue on purpose.</summary>
	Rebuild,
}

/// <summary>What happens to a claim being given back.</summary>
public enum SkipDisposition
{
	/// <summary>Hand it back for somebody else. The note stays in the queue.</summary>
	Release,

	/// <summary>It needs no enrichment -- a stub, an index page, a paste of raw output.</summary>
	NotWorthIndexing,

	/// <summary>It could not be made sense of. Counts toward the retry limit.</summary>
	Unreadable,
}

/// <summary>How much is left, on every result in the mode so the loop reports itself.</summary>
public sealed record IndexProgress
{
	public required int Total { get; init; }

	public required int Fresh { get; init; }

	public required int NeverIndexed { get; init; }

	public required int Stale { get; init; }

	public required int Claimed { get; init; }

	public required int Skipped { get; init; }

	public required int Failed { get; init; }

	/// <summary>What a run has left to do: everything not fresh, skipped or given up on.</summary>
	public required int Remaining { get; init; }
}

/// <summary>Whether there is work, and what it is.</summary>
public enum AssignmentState
{
	/// <summary>A note is claimed and returned.</summary>
	Assigned,

	/// <summary>Nothing left. The loop ends by itself.</summary>
	Drained,

	/// <summary>There is work, but somebody else holds all of it.</summary>
	Blocked,
}

/// <summary>
/// One claimed note, or the reason there is none.
/// <para>
/// A required state and an optional note, rather than an empty note meaning "done": a caller can
/// miss a null and cannot miss a discriminator it has to read.
/// </para>
/// </summary>
public sealed record NoteAssignment : NoteResult
{
	public required AssignmentState State { get; init; }

	public NoteWorkItem? Note { get; init; }

	public required IndexProgress Progress { get; init; }

	/// <summary>Why nothing was handed out, when the state is Blocked.</summary>
	public string? BlockedReason { get; init; }

	public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>
/// Everything needed to enrich one note, so the agent reads nothing else.
/// <para>
/// What it is handed is what it sees. An agent that went looking would produce output depending on
/// what it happened to find, and consistency across hundreds of notes is the whole value.
/// </para>
/// </summary>
public sealed record NoteWorkItem
{
	/// <summary>Presented back to note_index_write, which is how a claim is proved.</summary>
	public required string Lease { get; init; }

	public required DateTimeOffset Expires { get; init; }

	public required string Name { get; init; }

	public required string Path { get; init; }

	/// <summary>
	/// The note without the keys the indexer writes, so an agent re-enriching one is not anchored on
	/// the answer it is replacing.
	/// </summary>
	public required string Content { get; init; }

	public required StaleReason Reason { get; init; }

	public required int InboundLinks { get; init; }

	/// <summary>The topics already in use, with how many notes use each.</summary>
	public required IReadOnlyList<TopicUse> Vocabulary { get; init; }

	/// <summary>
	/// Accepted enrichments from this store. The anti-drift mechanism that survives a session ending:
	/// it lives in data handed over on every call rather than in a prompt that compaction may eat.
	/// </summary>
	public required IReadOnlyList<NoteExemplar> Exemplars { get; init; }

	/// <summary>Notes this one could link to, so the choice is made from a fixed set.</summary>
	public required IReadOnlyList<LinkCandidate> LinkCandidates { get; init; }

	/// <summary>What the note says now, when re-enriching. For comparing against, not continuing from.</summary>
	public Enrichment? Existing { get; init; }
}

public sealed record TopicUse
{
	public required string Name { get; init; }

	public required int Uses { get; init; }
}

public sealed record NoteExemplar
{
	public required string Name { get; init; }

	public required string Gist { get; init; }

	public required IReadOnlyList<string> Asks { get; init; }

	public required IReadOnlyList<string> Topics { get; init; }
}

public sealed record LinkCandidate
{
	public required string Name { get; init; }

	public required string Gist { get; init; }
}

/// <summary>An enrichment accepted and written into the note.</summary>
public sealed record EnrichmentAccepted : NoteResult
{
	public required string Name { get; init; }

	/// <summary>
	/// False where the note already said this, so nothing was written. Re-indexing an unchanged
	/// corpus touches no file, which on a synced store is the difference between free and a full
	/// replication.
	/// </summary>
	public required bool NoteRewritten { get; init; }

	public required IReadOnlyList<string> TopicsAdded { get; init; }

	/// <summary>
	/// Suggested links whose target is not a note here. Dropped rather than written: an unresolved
	/// wikilink in Obsidian is an invitation to create that note, and an indexer should not leave
	/// five hundred of those behind.
	/// </summary>
	public required IReadOnlyList<string> LinksDropped { get; init; }

	public required IndexProgress Progress { get; init; }

	public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>A claim given back.</summary>
public sealed record SkipRecorded : NoteResult
{
	public required string Name { get; init; }

	public required string Disposition { get; init; }

	public required IndexProgress Progress { get; init; }
}

/// <summary>How the run is going, and what the vocabulary looks like.</summary>
public sealed record IndexStatus : NoteResult
{
	public required IndexProgress Progress { get; init; }

	public required int SchemaVersion { get; init; }

	public required string PromptHash { get; init; }

	public required IReadOnlyList<TopicUse> Topics { get; init; }

	public required IReadOnlyList<LeaseHolder> Leases { get; init; }

	/// <summary>
	/// How the last few writes compare to the store's own norms. A new-topic rate that stays high
	/// after the first hundred notes means the vocabulary is being ignored, which is the leading
	/// indicator of drift -- it shows long before retrieval gets worse.
	/// </summary>
	public DriftReport? Drift { get; init; }

	public IReadOnlyList<string> Notices { get; init; } = [];
}

public sealed record LeaseHolder
{
	public required string Note { get; init; }

	public required string Run { get; init; }

	public required DateTimeOffset Expires { get; init; }
}

public sealed record DriftReport
{
	public required int Window { get; init; }

	public required double MeanGistLength { get; init; }

	public required double MeanAsks { get; init; }

	public required double MeanTopics { get; init; }

	/// <summary>Topics used once across the whole store, which is a topic nobody can filter by.</summary>
	public required int SingleUseTopics { get; init; }

	public required IReadOnlyList<string> Warnings { get; init; }
}
