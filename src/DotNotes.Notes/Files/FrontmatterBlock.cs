namespace DotNotes.Notes.Files;

/// <summary>
/// A note's file split into its YAML block and its prose, with the offsets a splice needs.
/// <para>
/// Offsets rather than a rebuilt string, because everything that writes a note puts back every byte
/// it did not mean to change. These are files a person edits in Obsidian, and a writer that
/// reconstructs the whole document reformats their quoting and drops their comments on every save.
/// </para>
/// </summary>
public readonly record struct FrontmatterBlock
{
	/// <summary>Whether the file opens with a YAML block at all.</summary>
	public required bool Present { get; init; }

	/// <summary>The YAML between the fences, without either fence and without a trailing break.</summary>
	public required string Yaml { get; init; }

	/// <summary>Everything after the closing fence.</summary>
	public required string Body { get; init; }

	/// <summary>Where <see cref="Yaml"/> starts in the file.</summary>
	public required int YamlStart { get; init; }

	/// <summary>How many characters of the file <see cref="Yaml"/> covers.</summary>
	public required int YamlLength { get; init; }

	/// <summary>
	/// The line ending the file uses. Preserved rather than imposed: rewriting a vault's notes from
	/// CRLF to LF is a diff on every line of every file, and on a synced drive it is that much
	/// traffic for nothing.
	/// </summary>
	public required string LineEnding { get; init; }

	/// <summary>
	/// Splits a note file.
	/// <para>
	/// A block is recognised only when the file opens with a fence and a closing fence is found, which
	/// is what Obsidian requires too. A file that opens with <c>---</c> and never closes it is all
	/// body: reading it as an unterminated block would swallow the note's prose into metadata and
	/// lose it on the next write.
	/// </para>
	/// </summary>
	public static FrontmatterBlock Split(string text)
	{
		var content = text.StartsWith('﻿') ? text[1..] : text;
		var lineEnding = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

		if (!OpensWithFence(content, lineEnding, out var yamlStart)) return None(content, lineEnding);

		var closing = ClosingFence(content, yamlStart);
		if (closing < 0) return None(content, lineEnding);

		var yamlLength = Math.Max(0, closing - yamlStart);
		var afterFence = closing + 3;

		// The break after the closing fence belongs to the fence, not to the body: keeping it would
		// give every note a leading blank line that grows by one on each rewrite.
		if (content.AsSpan(afterFence).StartsWith(lineEnding)) afterFence += lineEnding.Length;

		return new FrontmatterBlock
		{
			Present = true,
			Yaml = content.Substring(yamlStart, yamlLength).TrimEnd('\r', '\n'),
			Body = content[Math.Min(afterFence, content.Length)..],
			YamlStart = yamlStart,
			YamlLength = yamlLength,
			LineEnding = lineEnding,
		};
	}

	private static FrontmatterBlock None(string content, string lineEnding) => new()
	{
		Present = false,
		Yaml = string.Empty,
		Body = content,
		YamlStart = 0,
		YamlLength = 0,
		LineEnding = lineEnding,
	};

	/// <summary>Whether the file opens with <c>---</c> on a line of its own, and where the YAML starts.</summary>
	private static bool OpensWithFence(string content, string lineEnding, out int yamlStart)
	{
		yamlStart = 0;

		if (!content.StartsWith("---", StringComparison.Ordinal)) return false;

		var rest = content.AsSpan(3);

		if (!rest.StartsWith(lineEnding) && !rest.StartsWith("\n") && rest.Length != 0) return false;

		yamlStart = 3;
		if (content.AsSpan(yamlStart).StartsWith(lineEnding)) yamlStart += lineEnding.Length;
		else if (content.AsSpan(yamlStart).StartsWith("\n")) yamlStart += 1;

		return true;
	}

	/// <summary>
	/// Where the closing fence begins, or -1 for none. Either fence YAML accepts closes the block,
	/// and both have to sit alone at the start of a line -- a <c>---</c> indented or trailed by text
	/// is content.
	/// </summary>
	private static int ClosingFence(string content, int from)
	{
		for (var index = from; index < content.Length;)
		{
			var lineEnd = content.IndexOf('\n', index);
			var stop = lineEnd < 0 ? content.Length : lineEnd;
			var line = content.AsSpan(index, stop - index).TrimEnd('\r');

			if (line is "---" or "...") return index;
			if (lineEnd < 0) break;

			index = lineEnd + 1;
		}

		return -1;
	}
}
