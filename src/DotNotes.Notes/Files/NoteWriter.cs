using System.Globalization;

using DotNotes.Contracts;

namespace DotNotes.Notes.Files;

/// <summary>Composing a note's file from the fields a caller gave, without disturbing the rest.</summary>
public static class NoteWriter
{
	/// <summary>
	/// The fields this server owns and will rewrite. Anything else in a note's frontmatter belongs to
	/// the person who put it there and is spliced around rather than through.
	/// </summary>
	private static readonly string[] Authored =
		["name", "description", "scope", "type", "repository", "machines", "tags", "created", "updated", "superseded"];

	/// <summary>
	/// A note's new text.
	/// <para>
	/// Built by splicing, so replacing a note keeps the properties, comments and ordering a person
	/// added to it. A note that does not exist yet gets a file composed from these fields alone.
	/// </para>
	/// </summary>
	public static string Compose(NoteDraft draft, string? existing)
	{
		var lineEnding = existing is null
			? "\n"
			: FrontmatterBlock.Split(existing).LineEnding;

		var body = draft.Body.TrimEnd() + lineEnding;
		FrontmatterBlock? previous = existing is null ? null : FrontmatterBlock.Split(existing);
		var created = draft.Created ?? Created(previous) ?? draft.Today;

		var entries = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["name"] = FrontmatterSplice.Entry("name", draft.Name),
			["description"] = FrontmatterSplice.Entry("description", draft.Description),
			["scope"] = FrontmatterSplice.Entry("scope", draft.Scope.ToString().ToLowerInvariant()),
			["type"] = FrontmatterSplice.Entry("type", draft.Type.ToString().ToLowerInvariant()),
			["repository"] = FrontmatterSplice.Entry("repository", draft.Repository),
			["machines"] = Optional("machines", draft.Machines, lineEnding),

			// The author's tags, plus whatever topics the indexer has already written. Without the
			// merge, editing a note would silently strip its topics -- and nothing would report it,
			// because a note with no topics looks exactly like one that has not been enriched.
			["tags"] = Optional("tags", NoteFrontmatter.MergeTopics(draft.Tags, Topics(previous)), lineEnding),
			["created"] = FrontmatterSplice.Entry("created", Format(created)),
			["updated"] = FrontmatterSplice.Entry("updated", Format(draft.Today)),
		};

		// Only when given: a note kept in one store has no id, and a rewrite that is not about the
		// pairing leaves whatever id is there alone.
		if (draft.Id is { Length: > 0 } id) entries[NoteFrontmatter.IdKey] = FrontmatterSplice.Entry(NoteFrontmatter.IdKey, id);

		// The body is replaced wholesale and the frontmatter is not, so the splice runs over a file
		// carrying the new prose and the old metadata.
		var carrier = previous is { Present: true } block
			? string.Concat("---", lineEnding, block.Yaml, lineEnding, "---", lineEnding, body)
			: body;

		return FrontmatterSplice.Apply(carrier, entries);
	}

	/// <summary>
	/// Whether a note is longer than a note should be, and the headings to split it at.
	/// <para>
	/// One real memory in the store this replaces is 41 KB, which is not a note but a document
	/// nobody finishes. A ceiling is only enforceable because there is no append: a note cannot
	/// creep past it one call at a time.
	/// </para>
	/// </summary>
	public static string? TooLong(string body, int ceiling)
	{
		if (body.Length <= ceiling) return null;

		var headings = body.Split('\n')
			.Select(line => line.TrimEnd())
			.Where(line => line.StartsWith('#'))
			.Take(6)
			.ToArray();

		var advice = headings.Length > 0
			? $" Split it at its headings ({string.Join("; ", headings)}) and link the parts as [[name]]."
			: " Split it into notes that each answer one question, and link them as [[name]].";

		return $"That note is {body.Length} characters and the ceiling is {ceiling}." + advice;
	}

	/// <summary>Dates are written as dates, so Obsidian's properties pane offers a date picker.</summary>
	private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	/// <summary>The topics the indexer has written, which an authored write must not take with it.</summary>
	private static IReadOnlyList<string> Topics(FrontmatterBlock? previous) =>
		previous is { Present: true } block ? NoteFrontmatter.Parse(block.Yaml).Topics() : [];

	/// <summary>When the note first existed, kept across a rewrite rather than reset by one.</summary>
	private static DateOnly? Created(FrontmatterBlock? previous)
	{
		if (previous is not { Present: true } block) return null;

		var value = NoteFrontmatter.Parse(block.Yaml).Scalar("created");

		return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date) ? date : null;
	}

	/// <summary>
	/// A list, or the key's removal where there is nothing in it. An empty <c>tags: []</c> on every
	/// note is a property Obsidian shows in the pane and nobody wants to look at.
	/// </summary>
	private static string? Optional(string key, IReadOnlyList<string> values, string lineEnding) =>
		values.Count == 0 ? null : FrontmatterSplice.Entry(key, values, lineEnding);

	/// <summary>Every field this server writes, for a caller that needs to know what it owns.</summary>
	public static IReadOnlyList<string> AuthoredKeys => Authored;
}

/// <summary>What a caller asked to be written.</summary>
public sealed record NoteDraft
{
	public required string Name { get; init; }

	public required string Description { get; init; }

	public required string Body { get; init; }

	public required NoteScope Scope { get; init; }

	public required string Repository { get; init; }

	public NoteType Type { get; init; } = NoteType.Project;

	public IReadOnlyList<string> Tags { get; init; } = [];

	public IReadOnlyList<string> Machines { get; init; } = [];

	/// <summary>Today, injected so a write is reproducible in a test.</summary>
	public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);

	/// <summary>Overrides the creation date, which only an import has reason to do.</summary>
	public DateOnly? Created { get; init; }

	/// <summary>The pairing id for a note kept in both stores, or null to leave the file's own alone.</summary>
	public string? Id { get; init; }
}
