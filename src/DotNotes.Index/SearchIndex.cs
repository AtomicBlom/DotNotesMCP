using DotNotes.Contracts;

namespace DotNotes.Index;

/// <summary>
/// The whole index: a dictionary of term counts, held in memory and built by a crawl.
/// <para>
/// There is no index store. The notes are the truth, this is a pure cache of them, and at the size
/// a personal store reaches -- a few hundred notes, a few hundred kilobytes -- rebuilding it is
/// tens of milliseconds. A database would add a schema to migrate, a file that can disagree with
/// the notes, a question about where it lives so a sync service does not corrupt it, and for
/// SQLite, native assets per architecture on a machine that is x64 one day and ARM64 the next.
/// </para>
/// </summary>
public sealed class SearchIndex
{
	private readonly List<IndexedNote> _notes;
	private readonly Dictionary<string, int>[] _documentFrequency;
	private readonly double[] _averageLength;

	private SearchIndex(List<IndexedNote> notes)
	{
		_notes = notes;
		_documentFrequency = new Dictionary<string, int>[NoteFields.Count];
		_averageLength = new double[NoteFields.Count];

		foreach (var field in NoteFields.All)
		{
			var index = (int)field;

			_documentFrequency[index] = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (var note in notes)
			{
				foreach (var term in note.Counts[index].Keys)
				{
					_documentFrequency[index][term] = _documentFrequency[index].GetValueOrDefault(term) + 1;
				}
			}

			_averageLength[index] = notes.Count == 0
				? 0
				: notes.Average(note => (double)note.Lengths[index]);
		}
	}

	/// <summary>How many notes are in it.</summary>
	public int Count => _notes.Count;

	/// <summary>Every note, for a listing that is not a search.</summary>
	public IReadOnlyList<IndexedNote> Notes => _notes;

	/// <summary>
	/// Builds the index, and counts the links between the notes while every note is in hand. The
	/// count is what a hub bonus is computed from, and it cannot be known one note at a time.
	/// </summary>
	public static SearchIndex Build(IEnumerable<IndexedNote> notes)
	{
		var list = notes.ToList();
		var byName = new Dictionary<string, IndexedNote>(StringComparer.OrdinalIgnoreCase);

		foreach (var note in list) byName.TryAdd(note.Heading.Name, note);

		foreach (var note in list)
		{
			foreach (var link in note.Links)
			{
				var target = link.Target[(link.Target.LastIndexOf('/') + 1)..];

				if (byName.TryGetValue(target, out var linked) && !ReferenceEquals(linked, note))
				{
					linked.InboundLinks++;
				}
			}
		}

		return new SearchIndex(list);
	}

	/// <summary>
	/// Every note matching a query, best first, with the score it earned.
	/// <para>
	/// A note is a candidate when it matches any term, not all of them, because a two-word query
	/// where one word is rare is the common shape and demanding both would answer nothing. BM25
	/// already ranks a note matching both above a note matching one.
	/// </para>
	/// </summary>
	public IReadOnlyList<(IndexedNote Note, double Score)> Search(string? query, SearchWeighting weighting)
	{
		var terms = Tokenizer.Query(query);

		if (terms.Count == 0)
		{
			return [.. _notes
				.Select(note => (Note: note, Score: weighting.Multiplier(note)))
				.OrderByDescending(hit => hit.Score)
				.ThenBy(hit => hit.Note.Heading.Name, StringComparer.Ordinal)];
		}

		var hits = new List<(IndexedNote Note, double Score)>();

		foreach (var note in _notes)
		{
			var relevance = Relevance(note, terms);

			if (relevance > 0) hits.Add((note, relevance * weighting.Multiplier(note)));
		}

		return [.. hits
			.OrderByDescending(hit => hit.Score)
			.ThenBy(hit => hit.Note.Heading.Name, StringComparer.Ordinal)];
	}

	/// <summary>
	/// BM25 per field, summed with each field's weight. The same shape a full-text engine's
	/// <c>bm25()</c> computes, written out because the tokenizer feeding it is our own.
	/// </summary>
	private double Relevance(IndexedNote note, IReadOnlyList<string> terms)
	{
		var score = 0.0;

		foreach (var field in NoteFields.All)
		{
			var index = (int)field;
			var counts = note.Counts[index];

			if (counts.Count == 0) continue;

			var average = _averageLength[index];
			var weight = NoteFields.Weight(field);

			foreach (var term in terms)
			{
				if (!counts.TryGetValue(term, out var frequency)) continue;

				var documents = _documentFrequency[index].GetValueOrDefault(term);
				var inverse = Math.Log(1 + ((_notes.Count - documents + 0.5) / (documents + 0.5)));
				var normalised = average <= 0 ? 1 : note.Lengths[index] / average;
				var saturation = frequency * (NoteFields.K1 + 1)
					/ (frequency + (NoteFields.K1 * (1 - NoteFields.B + (NoteFields.B * normalised))));

				score += weight * inverse * saturation;
			}
		}

		return score;
	}
}

/// <summary>
/// What a hit is worth beyond how well its text matched.
/// <para>
/// Each of these is a tie-breaker rather than a decision. A note that plainly answers the question
/// has to win, and a bonus large enough to overturn that would be a bonus that hides the answer.
/// </para>
/// </summary>
public sealed record SearchWeighting
{
	/// <summary>The repository the caller is in, whose own notes are likelier to be the answer.</summary>
	public string? Repository { get; init; }

	/// <summary>Today, for the age term. Injected so the ranking is testable.</summary>
	public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);

	/// <summary>
	/// The multiplier for one note.
	/// <para>
	/// Linked notes get a logarithmic bonus, because link counts are power-law and a linear one
	/// would let a single hub win every query. Fifteen inbound links is about 1.6x, and closing a
	/// two-fold relevance gap would need roughly a hundred, which no note in a personal store has.
	/// Recency decays over a release cycle rather than a week. An unenriched note is penalised
	/// barely at all: it matched on its own words, which is real evidence, and a hard penalty would
	/// bury the note written five minutes ago.
	/// </para>
	/// </summary>
	public double Multiplier(IndexedNote note)
	{
		var links = 1 + (0.15 * Math.Log2(1 + note.InboundLinks));
		var age = note.Heading.Updated is { } updated
			? 1 + (0.10 * Math.Exp(-Math.Max(0, Today.DayNumber - updated.DayNumber) / 180.0))
			: 1.0;
		var scope = note.Heading.Scope == NoteScope.Repository && Repository is not null ? 1.25 : 1.0;
		var enriched = note.Heading.Enriched ? 1.0 : 0.9;

		return links * age * scope * enriched;
	}
}
