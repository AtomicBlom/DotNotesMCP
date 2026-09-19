using System.Text;

namespace DotNotes.Notes.Files;

/// <summary>
/// Replaces named keys in a note's frontmatter and leaves every other byte alone.
/// <para>
/// This is the one file where a mistake damages something a person wrote. Everything it does is
/// addressed by key: a key being replaced has its lines swapped in place, a key being removed has
/// its lines dropped, a key that is new is appended, and anything not named -- the person's own
/// properties, their comments, their blank lines, their choice of quoting -- comes out exactly as
/// it went in.
/// </para>
/// <para>
/// In place rather than appended-and-deduplicated, so the order a person put their properties in
/// survives. Obsidian shows them in file order, and having the machine's keys shuffle to the top on
/// each write would make every re-index a visible change to a file nobody edited.
/// </para>
/// </summary>
public static class FrontmatterSplice
{
	/// <summary>
	/// The file with <paramref name="replacements"/> applied. A null value removes the key; a value
	/// is a rendered entry, without a trailing break, which may span several lines.
	/// </summary>
	public static string Apply(string text, IReadOnlyDictionary<string, string?> replacements)
	{
		if (replacements.Count == 0) return text;

		var block = FrontmatterBlock.Split(text);

		return block.Present
			? text[..block.YamlStart] + Rewrite(block, replacements) + text[(block.YamlStart + block.YamlLength)..]
			: Create(block, replacements);
	}

	/// <summary>
	/// Renders one entry: <c>key: value</c> for a scalar, and a block sequence for a list, because
	/// Obsidian's properties pane edits a block list as a list and a flow list as a line of text.
	/// </summary>
	public static string Entry(string key, string value) => $"{key}: {YamlScalar.Render(value)}";

	/// <summary>Renders a list entry. An empty list renders as an empty block, which round-trips.</summary>
	public static string Entry(string key, IReadOnlyList<string> values, string lineEnding) =>
		values.Count == 0
			? $"{key}: []"
			: key + ":" + lineEnding
				+ string.Join(lineEnding, values.Select(value => $"  - {YamlScalar.Render(value)}"));

	/// <summary>The YAML block with the named keys replaced, removed or appended.</summary>
	private static string Rewrite(FrontmatterBlock block, IReadOnlyDictionary<string, string?> replacements)
	{
		var lines = block.Yaml.Split('\n');
		var written = new HashSet<string>(StringComparer.Ordinal);
		var builder = new StringBuilder();
		var index = 0;

		while (index < lines.Length)
		{
			var span = EntryAt(lines, index);

			if (span.Key is { } key && replacements.TryGetValue(key, out var replacement))
			{
				if (replacement is not null) Append(builder, replacement, block.LineEnding);

				written.Add(key);
				index = span.Next;
				continue;
			}

			for (var line = index; line < span.Next; line++)
			{
				Append(builder, lines[line].TrimEnd('\r'), block.LineEnding);
			}

			index = span.Next;
		}

		foreach (var (key, value) in replacements)
		{
			if (value is not null && !written.Contains(key)) Append(builder, value, block.LineEnding);
		}

		return builder.ToString();
	}

	/// <summary>A note with no frontmatter, given one.</summary>
	private static string Create(FrontmatterBlock block, IReadOnlyDictionary<string, string?> replacements)
	{
		var builder = new StringBuilder();

		builder.Append("---").Append(block.LineEnding);

		foreach (var (_, value) in replacements)
		{
			if (value is not null) Append(builder, value, block.LineEnding);
		}

		builder.Append("---").Append(block.LineEnding);

		return builder + block.Body;
	}

	/// <summary>
	/// The key at a line, and where the next top-level entry starts.
	/// <para>
	/// An entry owns every following line that is indented or blank, which is what carries a block
	/// sequence, a folded scalar and a wrapped value along with the key they belong to. A line that
	/// starts at column zero begins something else, and a line with no colon at column zero -- a
	/// document a person has half-edited -- is left exactly where it is rather than being attached to
	/// the key above it.
	/// </para>
	/// </summary>
	private static (string? Key, int Next) EntryAt(string[] lines, int index)
	{
		var key = KeyOf(lines[index]);
		var next = index + 1;

		while (next < lines.Length)
		{
			var line = lines[next].TrimEnd('\r');

			if (line.Length != 0 && !char.IsWhiteSpace(line[0])) break;

			next++;
		}

		return (key, next);
	}

	/// <summary>The key a top-level line declares, or null for a comment, a blank or a continuation.</summary>
	private static string? KeyOf(string line)
	{
		var text = line.TrimEnd('\r');

		if (text.Length == 0 || char.IsWhiteSpace(text[0]) || text[0] == '#') return null;

		var colon = text.IndexOf(':', StringComparison.Ordinal);
		if (colon <= 0) return null;

		return text[..colon].Trim().Trim('"', '\'');
	}

	private static void Append(StringBuilder builder, string entry, string lineEnding)
	{
		foreach (var line in entry.Split('\n'))
		{
			builder.Append(line.TrimEnd('\r')).Append(lineEnding);
		}
	}
}
