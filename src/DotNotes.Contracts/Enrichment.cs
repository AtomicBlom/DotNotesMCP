namespace DotNotes.Contracts;

/// <summary>
/// What an agent wrote about one note.
/// <para>
/// Document expansion rather than a summary: the job is to put into the note the words a future
/// query will use. The discriminating terms in a store like this are identifiers nobody types into
/// a search box, and <see cref="Asks"/> is what bridges that -- a question authored in the form a
/// query arrives in.
/// </para>
/// </summary>
public sealed record Enrichment
{
	/// <summary>One sentence naming what the note is about and what it says about it.</summary>
	public required string Gist { get; init; }

	/// <summary>Questions this note answers, in the words somebody who has not read it would use.</summary>
	public required IReadOnlyList<string> Asks { get; init; }

	/// <summary>Topics from the store's vocabulary.</summary>
	public required IReadOnlyList<string> Topics { get; init; }

	/// <summary>Proper nouns a search would type verbatim: types, products, people, error codes.</summary>
	public IReadOnlyList<string> Entities { get; init; } = [];

	/// <summary>Other names for this note's subject, for finding it by a name it does not carry.</summary>
	public IReadOnlyList<string> Aliases { get; init; } = [];

	/// <summary>Notes this one is about the same thing as, chosen from the candidates supplied.</summary>
	public IReadOnlyList<string> Links { get; init; } = [];

	/// <summary>How sure the agent is about the note, not about its summary of it.</summary>
	public string Confidence { get; init; } = "medium";
}

/// <summary>
/// The shape an enrichment has to have, enforced rather than requested.
/// <para>
/// Consistency across hundreds of notes is the whole value, and a prompt asking for it is a
/// suggestion where a validator is a contract. A refusal names exactly what to fix, so an agent
/// corrects in one turn rather than drifting.
/// </para>
/// </summary>
public static class EnrichmentShape
{
	/// <summary>A gist is read in a list, beside a dozen others. Past this it stops being scannable.</summary>
	public const int GistCeiling = 140;

	/// <summary>
	/// Three is enough to cover a note from more than one angle; past seven they stop being distinct
	/// and start diluting the field that matters most.
	/// </summary>
	public const int MinimumAsks = 3;

	public const int MaximumAsks = 7;

	public const int AskCeiling = 100;

	/// <summary>
	/// Two so a note is reachable by more than one facet, six because a note filed under more than
	/// that is filed under nothing.
	/// </summary>
	public const int MinimumTopics = 2;

	public const int MaximumTopics = 6;

	public const int MaximumAliases = 4;

	/// <summary>
	/// Openings that describe the note rather than its subject. A gist beginning "This note" spends
	/// its first words saying what the reader can already see.
	/// </summary>
	private static readonly string[] EmptyOpenings =
		["this note", "this document", "a note ", "a collection of", "notes on", "describes "];

	/// <summary>
	/// What is wrong with an enrichment, or null. Every message names the field and the fix, because
	/// the caller is a model that will act on it immediately.
	/// </summary>
	/// <param name="enrichment">What was submitted.</param>
	/// <param name="vocabulary">Topics already in use in this store.</param>
	/// <param name="declared">Topics the caller declared as new.</param>
	public static string? Fault(
		Enrichment enrichment,
		IReadOnlyCollection<string> vocabulary,
		IReadOnlyCollection<string> declared)
	{
		if (string.IsNullOrWhiteSpace(enrichment.Gist)) return "gist is required.";

		if (enrichment.Gist.Length > GistCeiling)
		{
			return $"gist is {enrichment.Gist.Length} characters; the ceiling is {GistCeiling}.";
		}

		var opening = EmptyOpenings.FirstOrDefault(
			start => enrichment.Gist.StartsWith(start, StringComparison.OrdinalIgnoreCase));

		if (opening is not null)
		{
			return $"gist starts with \"{opening.Trim()}\". Start with the subject instead.";
		}

		if (enrichment.Asks.Count is < MinimumAsks or > MaximumAsks)
		{
			return $"asks has {enrichment.Asks.Count} entries; give between {MinimumAsks} and {MaximumAsks}.";
		}

		foreach (var ask in enrichment.Asks)
		{
			if (!ask.TrimEnd().EndsWith('?')) return $"asks entry \"{ask}\" is not a question.";
			if (ask.Length > AskCeiling) return $"asks entry \"{ask}\" is longer than {AskCeiling} characters.";
		}

		if (enrichment.Topics.Count is < MinimumTopics or > MaximumTopics)
		{
			return $"topics has {enrichment.Topics.Count} entries; give between {MinimumTopics} and "
				+ $"{MaximumTopics}.";
		}

		if (enrichment.Aliases.Count > MaximumAliases)
		{
			return $"aliases has {enrichment.Aliases.Count} entries; give at most {MaximumAliases}.";
		}

		return Vocabulary(enrichment.Topics, vocabulary, declared);
	}

	/// <summary>
	/// The refusal that keeps the vocabulary closed.
	/// <para>
	/// Without it, five hundred notes produce five hundred topics and the facet is worthless. The
	/// point is not to forbid a new topic but to make adding one deliberate: declaring it costs one
	/// argument, and a topic that arrives by accident never does.
	/// </para>
	/// </summary>
	private static string? Vocabulary(
		IReadOnlyList<string> topics,
		IReadOnlyCollection<string> vocabulary,
		IReadOnlyCollection<string> declared)
	{
		foreach (var topic in topics)
		{
			if (topic != topic.ToLowerInvariant() || topic.Contains(' ', StringComparison.Ordinal))
			{
				return $"topic \"{topic}\" must be lower case and hyphenated.";
			}

			var known = vocabulary.Contains(topic, StringComparer.OrdinalIgnoreCase)
				|| declared.Contains(topic, StringComparer.OrdinalIgnoreCase);

			if (!known)
			{
				return $"topic \"{topic}\" is not in this store's vocabulary. Prefer one that is, or "
					+ "list it in newTopics to add it deliberately.";
			}
		}

		return null;
	}
}
