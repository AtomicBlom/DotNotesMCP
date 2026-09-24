using YamlDotNet.Core;

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

	/// <summary>
	/// The prefix on a tag the indexing mode owns, inside Obsidian's own <c>tags</c>.
	/// <para>
	/// Topics live there rather than under a key of their own because a key of their own is invisible
	/// to Obsidian: no tag pane, no node in the graph, nothing to filter a Bases view by. Since the
	/// point of pointing the store at a vault is that the person gets something from it, a topic they
	/// cannot see is a topic that does not exist for them.
	/// </para>
	/// <para>
	/// Nested, so the tag pane groups every machine topic under one collapsible <c>dn</c> and the
	/// person's own tags stay theirs. The prefix is also what makes the merge tractable: a write
	/// replaces the prefixed entries and returns every other tag exactly as it was.
	/// </para>
	/// </summary>
	public const string TopicPrefix = "dn/";

	/// <summary>
	/// Every topic this note is filed under, prefix removed.
	/// <para>
	/// The author's own tags count. They are the same kind of thing as a topic -- a word this note is
	/// about -- and treating them as a separate vocabulary would mean the indexer declaring a topic
	/// as new when the person had already used it.
	/// </para>
	/// </summary>
	public IReadOnlyList<string> Topics() =>
		[.. Sequence("tags").Select(Unprefixed).Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>
	/// A note's <c>tags</c> with the given topics in it, the author's own entries kept in the order
	/// they were in, and nothing duplicated.
	/// <para>
	/// A topic the author already wrote as a plain tag is left alone rather than added again with a
	/// prefix. Two entries for one word is two nodes in the graph and two rows in the tag pane, for a
	/// distinction the reader does not care about -- and the prefix is there to mark what the indexer
	/// added, not to claim what it merely agreed with.
	/// </para>
	/// <para>
	/// Deterministic, so re-indexing an unchanged note rewrites nothing.
	/// </para>
	/// </summary>
	public static IReadOnlyList<string> MergeTopics(
		IReadOnlyList<string> tags,
		IReadOnlyList<string> topics)
	{
		var authored = tags
			.Where(tag => !tag.StartsWith(TopicPrefix, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		var already = authored.ToHashSet(StringComparer.OrdinalIgnoreCase);

		return
		[
			.. authored,
			.. topics.Where(topic => !already.Contains(topic)).Select(topic => TopicPrefix + topic),
		];
	}

	/// <summary>A tag without the prefix that marks it as the indexer's.</summary>
	private static string Unprefixed(string tag) =>
		tag.StartsWith(TopicPrefix, StringComparison.OrdinalIgnoreCase) ? tag[TopicPrefix.Length..] : tag;

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
			return new NoteFrontmatter(YamlBlock.Read(yaml), error: null);
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
		_values.GetValueOrDefault(key) is IDictionary<string, object?> map
			&& map.TryGetValue(inner, out var value)
				? value as string
				: null;

	/// <summary>
	/// The key joining a note kept in both stores to its other copy. Server-owned, so it is outside
	/// the source hash like every <c>dn-</c> key, and not enrichment, so it does not make a note read
	/// as enriched.
	/// </summary>
	public const string IdKey = "dn-id";

	/// <summary>
	/// Whether this note carries enrichment written by the indexing mode. The pairing id is not
	/// enrichment: counting it would mark every note kept in both stores as indexed when it never was.
	/// </summary>
	public bool IsEnriched => _values.Keys.Any(key =>
		key.StartsWith(ServerPrefix, StringComparison.Ordinal) && !key.Equals(IdKey, StringComparison.Ordinal));
}
