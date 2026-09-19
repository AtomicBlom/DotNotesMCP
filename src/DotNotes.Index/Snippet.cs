using System.Text;

namespace DotNotes.Index;

/// <summary>
/// The window of a note that goes back with a hit.
/// <para>
/// This is what makes a search cheap enough to be worth making on a hunch. Ten notes returned whole
/// is tens of thousands of tokens spent on nine the caller will discard; ten extracts is a page. A
/// caller that wants the note asks for it by name.
/// </para>
/// </summary>
public static class Snippet
{
	/// <summary>How much of a note a hit carries.</summary>
	public const int Length = 200;

	/// <summary>
	/// How far a cut will look for a word boundary before giving up and cutting mid-word. Thirty
	/// characters is longer than any ordinary word and far shorter than the runs that have no
	/// boundary at all.
	/// </summary>
	private const int Overrun = 30;

	/// <summary>
	/// The passage around the best cluster of query terms, or the note's opening where none matched.
	/// <para>
	/// Around the terms rather than from the start, because the sentence that explains the match is
	/// what tells a reader whether to open the note, and it is rarely the first one.
	/// </para>
	/// </summary>
	public static string Of(string body, IReadOnlyList<string> terms)
	{
		var text = Flatten(body);

		if (text.Length <= Length) return text;
		if (terms.Count == 0) return Cut(text, 0);

		var best = BestOffset(text, terms);

		return Cut(text, best);
	}

	/// <summary>
	/// Where the densest run of matches starts. A crude scan over word starts rather than an index:
	/// this runs on the handful of notes that are about to be returned, not on the store.
	/// </summary>
	private static int BestOffset(string text, IReadOnlyList<string> terms)
	{
		var lowered = text.ToLowerInvariant();
		var best = 0;
		var bestHits = 0;

		for (var start = 0; start < lowered.Length; start += Length / 2)
		{
			var window = lowered.AsSpan(start, Math.Min(Length, lowered.Length - start));
			var hits = 0;

			// A loop rather than a Count, because a span cannot be captured by a lambda.
			foreach (var term in terms)
			{
				if (window.Contains(term, StringComparison.Ordinal)) hits++;
			}

			if (hits <= bestHits) continue;

			bestHits = hits;
			best = start;
		}

		return best;
	}

	/// <summary>
	/// A passage of about the right length, starting and ending at word boundaries, with an ellipsis
	/// wherever it was cut.
	/// </summary>
	private static string Cut(string text, int offset)
	{
		var start = offset;

		// Back up to a word boundary so a passage never opens mid-identifier, which reads as a typo
		// -- but only so far. A note carrying a hash, a URL or a minified line has runs with no
		// boundary in them at all, and an unbounded search walks to the start of the note and
		// returns that run instead of the text that matched.
		var floor = Math.Max(0, start - Overrun);

		while (start > floor && !char.IsWhiteSpace(text[start - 1])) start--;

		var end = Math.Min(text.Length, start + Length);
		var ceiling = Math.Min(text.Length, end + Overrun);

		while (end < ceiling && !char.IsWhiteSpace(text[end])) end++;

		var passage = text[start..end].Trim();
		var opened = start > 0 ? "…" : string.Empty;
		var closed = end < text.Length ? "…" : string.Empty;

		return opened + passage + closed;
	}

	/// <summary>
	/// A note's prose as one line, with the markup that only means something in a renderer removed.
	/// An extract is read inside a tool result, where a heading marker and a fence are noise.
	/// </summary>
	private static string Flatten(string body)
	{
		var builder = new StringBuilder(body.Length);
		var space = true;

		foreach (var character in body)
		{
			if (char.IsWhiteSpace(character))
			{
				if (!space) builder.Append(' ');

				space = true;
				continue;
			}

			// Paired markers go wherever they are. Dropping one only where it follows a space takes
			// the opening backtick of `dotnet build` and leaves the closing one, which reads as a
			// typo in a result a model will quote back to somebody.
			if (character is '`' or '*') continue;

			// Block markers are only markers at the start of a line. An underscore is never dropped:
			// it is part of snake_case_names, which are exactly the terms these notes are about.
			if (character is '#' or '>' && space) continue;

			builder.Append(character);
			space = false;
		}

		return builder.ToString().Trim();
	}
}
