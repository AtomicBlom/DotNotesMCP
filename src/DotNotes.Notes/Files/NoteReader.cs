using System.Globalization;

using DotNotes.Contracts;

namespace DotNotes.Notes.Files;

/// <summary>
/// One note's file, read into the shape the rest of the server works in.
/// <para>
/// Nothing here refuses. A note missing its name is named after its file, a note missing a
/// description gets its first line, an unrecognised type is a project note, and a broken YAML block
/// leaves a note with a body and nothing else. A store is a folder a person edits by hand, so every
/// field has to survive not being there -- and a note that cannot be read is a note that cannot be
/// found, which is worse than one read imperfectly. What is wrong is reported by
/// <c>note_check</c> rather than by refusing to answer.
/// </para>
/// </summary>
public static class NoteReader
{
	/// <summary>A note, or null where the file is not there.</summary>
	public static Note? Read(string path, NoteScope scope)
	{
		var content = NoteFile.Read(path);

		return content is null ? null : Parse(path, scope, content);
	}

	/// <summary>A note from text already in hand.</summary>
	public static Note Parse(string path, NoteScope scope, string content)
	{
		var block = FrontmatterBlock.Split(content);
		var matter = NoteFrontmatter.Parse(block.Yaml);
		var body = block.Body;

		return new Note
		{
			Heading = new NoteHeading
			{
				Name = Slug.Of(matter.Scalar("name") ?? Path.GetFileNameWithoutExtension(path)),
				Description = matter.Scalar("description") ?? FirstLine(body),
				Scope = scope,
				Type = TypeOf(matter.Scalar("type") ?? matter.Nested("metadata", "type")),
				Tags = matter.Sequence("tags"),
				Machines = matter.Sequence("machines"),
				Path = path,
				Revision = NoteFile.Revision(content),
				Created = DateOf(matter.Scalar("created")),
				Updated = DateOf(matter.Scalar("updated")),
				Gist = matter.Scalar("dn-gist"),
				Enriched = matter.IsEnriched,
				Superseded = matter.Scalar("superseded") is { Length: > 0 } superseded ? superseded : null,
				Id = matter.Scalar(NoteFrontmatter.IdKey),
			},
			Body = body,
			Content = content,
			Frontmatter = matter,
			Links = Wikilink.In(body),
		};
	}

	/// <summary>
	/// The type a note declares. An unrecognised one is a project note rather than a refusal,
	/// because this is a value a person typed into a file and the cost of getting it wrong is a
	/// facet, not a lost note. A tool argument is the opposite and refuses.
	/// </summary>
	private static NoteType TypeOf(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"user" => NoteType.User,
		"feedback" => NoteType.Feedback,
		"reference" => NoteType.Reference,
		_ => NoteType.Project,
	};

	/// <summary>A date, or null for anything that is not one. A malformed date is no date.</summary>
	private static DateOnly? DateOf(string? value) =>
		DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date) ? date : null;

	/// <summary>
	/// The first line of prose, for a note whose author wrote no description. Better than an empty
	/// string in a listing, which reads as a note with nothing in it.
	/// </summary>
	private static string FirstLine(string body)
	{
		foreach (var raw in body.Split('\n'))
		{
			var line = raw.Trim().TrimStart('#', '>', '-', '*', ' ');

			if (line.Length > 0) return line.Length > 200 ? line[..200].TrimEnd() + "…" : line;
		}

		return string.Empty;
	}
}

/// <summary>A note as read: what a listing needs, plus the parts only a reader of the whole note needs.</summary>
public sealed record Note
{
	public required NoteHeading Heading { get; init; }

	/// <summary>The prose, without the frontmatter block.</summary>
	public required string Body { get; init; }

	/// <summary>The whole file, which is what a hash and a rewrite are taken over.</summary>
	public required string Content { get; init; }

	/// <summary>The parsed metadata, including any keys this server does not know about.</summary>
	public required NoteFrontmatter Frontmatter { get; init; }

	/// <summary>The links out of this note, in the order they appear.</summary>
	public required IReadOnlyList<Wikilink> Links { get; init; }
}
