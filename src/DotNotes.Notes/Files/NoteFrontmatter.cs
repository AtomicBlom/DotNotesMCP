using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace DotNotes.Notes.Files;

/// <summary>
/// A note's frontmatter, read.
/// <para>
/// Read with a real YAML parser and written by splicing text, which is not inconsistency: the
/// parser is what understands the quoting, the block scalars and the flow sequences a person may
/// have typed, and text is what puts back the bytes nobody asked to change.
/// </para>
/// </summary>
public sealed class NoteFrontmatter
{
	/// <summary>The prefix on every key this server owns, and writes.</summary>
	public const string ServerPrefix = "dn-";

	private static readonly IDeserializer Reader = new DeserializerBuilder().Build();

	private readonly Dictionary<string, object?> _values;

	private NoteFrontmatter(Dictionary<string, object?> values, string? error)
	{
		_values = values;
		Error = error;
	}

	/// <summary>
	/// What the parser objected to, or null. Recorded rather than thrown, because a note is data: one
	/// broken block must not take out a search across a whole store, and the prose underneath it is
	/// still worth finding. <c>note_check</c> is what reports these.
	/// </summary>
	public string? Error { get; }

	/// <summary>Every key present, in the order the parser returned them.</summary>
	public IReadOnlyCollection<string> Keys => _values.Keys;

	public static NoteFrontmatter Parse(string yaml)
	{
		if (string.IsNullOrWhiteSpace(yaml)) return new NoteFrontmatter([], error: null);

		try
		{
			var parsed = Reader.Deserialize<Dictionary<string, object?>>(yaml);

			return new NoteFrontmatter(parsed ?? [], error: null);
		}
		catch (YamlException exception)
		{
			return new NoteFrontmatter([], $"line {exception.Start.Line}: {exception.Message}");
		}
	}

	/// <summary>A single value, or null where the key is absent or holds a list.</summary>
	public string? Scalar(string key) => _values.GetValueOrDefault(key) as string;

	/// <summary>
	/// A list of values. A key holding one scalar reads as a list of one, because
	/// <c>tags: arm64</c> and <c>tags: [arm64]</c> are the same intent and Obsidian accepts both.
	/// </summary>
	public IReadOnlyList<string> Sequence(string key) => _values.GetValueOrDefault(key) switch
	{
		string single when single.Length > 0 => [single],
		IEnumerable<object?> items => [.. items.Select(item => item?.ToString() ?? string.Empty)
			.Where(item => item.Length > 0)],
		_ => [],
	};

	/// <summary>Whether a key is there at all, whatever it holds.</summary>
	public bool Has(string key) => _values.ContainsKey(key);

	/// <summary>
	/// A value one level down, for the dialect Claude Code's own memory writes: <c>type</c> under a
	/// <c>metadata</c> map rather than at the top level.
	/// <para>
	/// Read, never written. A note copied into a store by hand should keep the type its author gave
	/// it rather than silently becoming a project note, and the corpus this replaces is all written
	/// that way. New notes get a flat key, because that is the one Obsidian's properties pane can
	/// show and edit.
	/// </para>
	/// </summary>
	public string? Nested(string key, string inner) =>
		_values.GetValueOrDefault(key) is IDictionary<object, object> map
			&& map.TryGetValue(inner, out var value)
				? value as string
				: null;

	/// <summary>Whether this note carries enrichment written by the indexing mode.</summary>
	public bool IsEnriched => _values.Keys.Any(key => key.StartsWith(ServerPrefix, StringComparison.Ordinal));
}
