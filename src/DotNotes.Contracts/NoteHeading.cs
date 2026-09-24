namespace DotNotes.Contracts;

/// <summary>
/// Everything about a note except its body.
/// <para>
/// The unit a listing and a search hit are made of, and the reason both are cheap. Ten hits
/// returned as whole notes is most of a working context spent on nine the caller will discard; ten
/// headings is a page. A caller that wants the note asks for it by name.
/// </para>
/// </summary>
public sealed record NoteHeading
{
	/// <summary>The slug the note is addressed and linked by.</summary>
	public required string Name { get; init; }

	/// <summary>The one line the author wrote about what this note says.</summary>
	public required string Description { get; init; }

	public required NoteScope Scope { get; init; }

	public required NoteType Type { get; init; }

	/// <summary>The author's own tags. Distinct from the topics the indexing mode writes.</summary>
	public IReadOnlyList<string> Tags { get; init; } = [];

	/// <summary>
	/// The machines this note is true of, or empty for all of them. It carries the x64-versus-ARM64
	/// split, and a note pinned to another machine is shown with a flag rather than hidden, because
	/// the other machine's quirk is often exactly what is being looked for.
	/// </summary>
	public IReadOnlyList<string> Machines { get; init; } = [];

	/// <summary>Where the file is, for a person who wants to open it.</summary>
	public required string Path { get; init; }

	/// <summary>
	/// What a caller quotes back to prove it has seen the note it is replacing. A write over an
	/// existing note carries this and is refused if the note changed underneath.
	/// </summary>
	public required string Revision { get; init; }

	public DateOnly? Created { get; init; }

	public DateOnly? Updated { get; init; }

	/// <summary>The one-line summary the indexing mode wrote, where it has run.</summary>
	public string? Gist { get; init; }

	/// <summary>Whether the indexing mode has written enrichment for this note.</summary>
	public bool Enriched { get; init; }

	/// <summary>
	/// What retired this note, where it has been: a reason, or the <c>[[replacement]]</c>. A
	/// superseded note is left out of a search and still read whole by name.
	/// </summary>
	public string? Superseded { get; init; }

	/// <summary>
	/// The other store, where this note is kept in both and the copy there is the same note. Null
	/// for a note kept in one.
	/// </summary>
	public NoteScope? Twin { get; init; }

	/// <summary>
	/// What joins a note kept in both stores to its other copy, written as <c>dn-id</c>. Not sent: a
	/// caller acts on <see cref="Twin"/>, and an identifier it cannot use is bytes on every hit.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string? Id { get; init; }
}
