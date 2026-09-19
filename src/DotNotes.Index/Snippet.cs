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

		// Back up to a word boundary so a passage never opens mid-identifier, which reads as a typo.
		while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;

		var end = Math.Min(text.Length, start + Length);

		while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

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

			if (character is '#' or '`' or '>' or '*' or '_' && space) continue;

			builder.Append(character);
			space = false;
		}

		return builder.ToString().Trim();
	}
}
