using System.Text;

namespace DotNotes.Index;

/// <summary>
/// Text broken into the terms a search matches on.
/// <para>
/// It emits the whole identifier <em>and</em> its parts, which is the reason this is written rather
/// than taken from a library. The terms that tell notes about this work apart are
/// <c>GeneratedBindableCustomProperty</c>, <c>IBindableCustomPropertyImplementation</c>,
/// <c>Db.Primary</c>, <c>TenantId</c> -- and somebody searching later types "bindable property".
/// A tokenizer that keeps identifiers whole never finds the note; one that only splits them stops
/// matching the exact name, which is the strongest signal there is. Emitting both costs a longer
/// posting list and finds the note either way.
/// </para>
/// </summary>
public static class Tokenizer
{
	/// <summary>
	/// Characters that join an identifier rather than ending one. Splitting on these as well as
	/// keeping the whole is what makes <c>Db.Primary</c> reachable by <c>db.primary</c>, by
	/// <c>primary</c>, and by neither of the halves of an unrelated note that merely says "db".
	/// </summary>
	private const string Joiners = "._-";

	/// <summary>Every term in a piece of text, lower-cased, in order, with repeats kept.</summary>
	public static IReadOnlyList<string> Of(string? text)
	{
		var terms = new List<string>();

		if (string.IsNullOrEmpty(text)) return terms;

		foreach (var word in Words(text))
		{
			Expand(word, terms);
		}

		return terms;
	}

	/// <summary>The distinct terms of a query, which is the same reading with repeats dropped.</summary>
	public static IReadOnlyList<string> Query(string? text) =>
		[.. Of(text).Distinct(StringComparer.Ordinal)];

	/// <summary>
	/// The runs of a document that could be an identifier: letters, digits and joiners. Everything
	/// else is punctuation, and a joiner at either end of a run is punctuation too -- a sentence
	/// ending in a full stop must not make the last word a different term from the same word
	/// mid-sentence.
	/// </summary>
	private static IEnumerable<string> Words(string text)
	{
		var word = new StringBuilder();

		foreach (var character in text)
		{
			if (char.IsLetterOrDigit(character) || Joiners.Contains(character, StringComparison.Ordinal))
			{
				word.Append(character);
				continue;
			}

			if (word.Length > 0)
			{
				yield return word.ToString();
				word.Clear();
			}
		}

		if (word.Length > 0) yield return word.ToString();
	}

	/// <summary>
	/// One run, as every term it should be findable by: itself, its joiner-separated segments, and
	/// the words inside each segment.
	/// </summary>
	private static void Expand(string word, List<string> terms)
	{
		var trimmed = word.Trim(Joiners.ToCharArray());
		if (trimmed.Length == 0) return;

		Add(terms, trimmed);

		var segments = trimmed.Split(Joiners.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

		foreach (var segment in segments)
		{
			// Only when the run actually had joiners, or this repeats what was just added.
			if (segments.Length > 1) Add(terms, segment);

			foreach (var part in Parts(segment))
			{
				if (!part.Equals(segment, StringComparison.OrdinalIgnoreCase)) Add(terms, part);
			}
		}
	}

	/// <summary>
	/// The words inside one segment, split where a reader would see a word boundary: at a change of
	/// case, at the end of an acronym, and between letters and digits.
	/// <para>
	/// The acronym rule is the one worth spelling out. <c>XMLHttpRequest</c> breaks before the
	/// <c>H</c> and not before the <c>T</c>, because a run of capitals followed by a lower-case
	/// letter belongs to the word that lower-case letter starts. Without it the segment yields
	/// <c>xmlhttp</c> and nobody's query says that.
	/// </para>
	/// </summary>
	private static IEnumerable<string> Parts(string segment)
	{
		var start = 0;

		for (var index = 1; index < segment.Length; index++)
		{
			if (!IsBoundary(segment, index)) continue;

			yield return segment[start..index];
			start = index;
		}

		if (start < segment.Length) yield return segment[start..];
	}

	private static bool IsBoundary(string segment, int index)
	{
		var previous = segment[index - 1];
		var current = segment[index];

		if (char.IsLower(previous) && char.IsUpper(current)) return true;
		if (char.IsLetter(previous) != char.IsLetter(current)) return true;

		var startsWord = char.IsUpper(previous)
			&& char.IsUpper(current)
			&& index + 1 < segment.Length
			&& char.IsLower(segment[index + 1]);

		return startsWord;
	}

	private static void Add(List<string> terms, string term)
	{
		if (term.Length > 0) terms.Add(term.ToLowerInvariant());
	}
}
