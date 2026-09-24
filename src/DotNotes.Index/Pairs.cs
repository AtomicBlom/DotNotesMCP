using DotNotes.Contracts;
using DotNotes.Notes.Stores;

namespace DotNotes.Index;

/// <summary>
/// Which notes are kept in both stores, and which of those the committed copy has retired, across
/// every store a call can read.
/// <para>
/// Built from every store rather than from the ones a query covers, because a note's twin is a fact
/// about the other store: a search of machine scope still has to say that a hit is also committed,
/// and still has to leave out a private copy whose committed copy was superseded before that
/// retirement reached it.
/// </para>
/// </summary>
internal sealed class Pairs
{
	private readonly HashSet<string> _machine = new(StringComparer.Ordinal);
	private readonly HashSet<string> _repository = new(StringComparer.Ordinal);
	private readonly HashSet<string> _retired = new(StringComparer.Ordinal);

	/// <summary>The pairing across every store, from the indexes a search already holds.</summary>
	public static Pairs Of(NoteStores stores, Func<NoteStore, SearchIndex> index)
	{
		var pairs = new Pairs();

		foreach (var store in stores.Reading(StoreSelection.Both))
		{
			foreach (var note in index(store).Notes)
			{
				if (note.Heading.Id is not { Length: > 0 } id) continue;

				(store.Scope == NoteScope.Repository ? pairs._repository : pairs._machine).Add(id);

				if (store.Scope == NoteScope.Repository && note.Heading.Superseded is not null) pairs._retired.Add(id);
			}
		}

		return pairs;
	}

	/// <summary>A heading with its twin named, where the other store holds the same note.</summary>
	public NoteHeading Marked(NoteHeading heading)
	{
		if (heading.Id is not { Length: > 0 } id) return heading;

		var other = heading.Scope == NoteScope.Repository ? _machine : _repository;

		return other.Contains(id)
			? heading with { Twin = heading.Scope == NoteScope.Repository ? NoteScope.Machine : NoteScope.Repository }
			: heading;
	}

	/// <summary>
	/// Whether a note is out of a search: superseded itself, or the private copy of a committed note
	/// that was, before this machine marked it.
	/// </summary>
	public bool IsRetired(NoteHeading heading) =>
		heading.Superseded is not null || (heading.Id is { Length: > 0 } id && _retired.Contains(id));
}
