using DotNotes.Contracts;

namespace DotNotes.Notes.Stores;

/// <summary>
/// Finding notes, behind one interface.
/// <para>
/// The seam exists so that what answers a query can be replaced without touching a tool, a store or
/// a result shape. Today it is a dictionary built by a crawl; the evidence that would justify
/// something else -- a persisted index, or vectors -- is a cold start a caller can feel, or queries
/// that miss on paraphrase no authored question reaches. Every hit says which implementation
/// answered, so the swap is visible rather than assumed.
/// </para>
/// </summary>
public interface INoteSearch
{
	/// <summary>What this implementation is called, reported on every result.</summary>
	string Backend { get; }

	/// <summary>
	/// The notes matching a query, best first. An empty query lists, which is why there is no
	/// separate listing tool.
	/// </summary>
	IReadOnlyList<NoteHit> Search(NoteStores stores, NoteQuery query);

	/// <summary>One note whole, or null where the name names nothing in the scopes searched.</summary>
	NoteHit? Find(NoteStores stores, string name, StoreSelection scope);

	/// <summary>Everything known about the notes in a store, for a listing or a report.</summary>
	IReadOnlyList<NoteHeading> Headings(NoteStores stores, StoreSelection scope);
}

/// <summary>What a caller asked for.</summary>
public sealed record NoteQuery
{
	/// <summary>The words to match. Empty lists rather than answering nothing.</summary>
	public string? Text { get; init; }

	public StoreSelection Scope { get; init; } = StoreSelection.Both;

	/// <summary>Only notes of this kind, or all of them.</summary>
	public NoteType? Type { get; init; }

	/// <summary>Only notes carrying all of these tags.</summary>
	public IReadOnlyList<string> Tags { get; init; } = [];

	/// <summary>
	/// How many hits to return. Ten is what a caller can read; fifty is the most that is ever worth
	/// sending, because a result nobody reads is context spent for nothing.
	/// </summary>
	public int Limit { get; init; } = 10;
}

/// <summary>One note, as a search answers.</summary>
public sealed record NoteHit
{
	public required NoteHeading Heading { get; init; }

	/// <summary>
	/// A window of the note's own text around what matched, or its opening where nothing did. Never
	/// the whole note: ten notes returned whole is most of a working context spent on nine the
	/// caller will discard, and that cost is what stops a search being worth making speculatively.
	/// </summary>
	public required string Extract { get; init; }

	public required double Score { get; init; }

	/// <summary>
	/// True where the note names machines and this is not one of them. Flagged rather than hidden,
	/// because the other machine's quirk is often exactly what is being looked for.
	/// </summary>
	public bool OtherMachine { get; init; }
}

