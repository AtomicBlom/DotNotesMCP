using System.Text.RegularExpressions;

namespace DotNotes.Notes.Files;

/// <summary>
/// Where the code is in a markdown document, so that what is quoted is not read as if it were meant.
/// <para>
/// A note about this server quotes <c>[[name]]</c> in a fenced block to explain the syntax, and a
/// note about markup quotes brackets for their own sake. Counting those produces links nobody wrote:
/// dangling targets in <c>note_check</c>, and backlinks between notes that never mention each other.
/// </para>
/// </summary>
public static partial class Code
{
	/// <summary>A half-open range of the document.</summary>
	public readonly record struct Span(int Start, int End);

	/// <summary>
	/// Every fenced block and every inline span, in order. Fences are matched first, so a backtick
	/// inside a fenced block does not open an inline span that swallows the rest of the note.
	/// </summary>
	public static IReadOnlyList<Span> Spans(string text)
	{
		var spans = new List<Span>();

		foreach (Match match in Fenced().Matches(text))
		{
			spans.Add(new Span(match.Index, match.Index + match.Length));
		}

		foreach (Match match in Inline().Matches(text))
		{
			var inside = spans.Any(span => match.Index >= span.Start && match.Index < span.End);

			if (!inside) spans.Add(new Span(match.Index, match.Index + match.Length));
		}

		return spans;
	}

	/// <summary>
	/// A fenced block, opened by at least three backticks or tildes at the start of a line.
	/// <para>
	/// The second alternative is what makes an unterminated fence run to the end of the document, as
	/// a renderer treats it. It cannot be <c>$</c>: under <see cref="RegexOptions.Multiline"/> that
	/// is the end of a line, so an unclosed fence would cover only the three backticks that opened
	/// it and everything a person meant as code would be read as prose.
	/// </para>
	/// </summary>
	[GeneratedRegex(@"^(?<fence>`{3,}|~{3,})[^\r\n]*(?:\r?\n[\s\S]*?^\k<fence>[^\r\n]*|[\s\S]*)",
		RegexOptions.Multiline | RegexOptions.Compiled)]
	private static partial Regex Fenced();

	/// <summary>An inline span, which never crosses a blank line.</summary>
	[GeneratedRegex(@"(?<ticks>`+)[^\r\n]*?\k<ticks>", RegexOptions.Compiled)]
	private static partial Regex Inline();
}
