using DotNotes.Contracts;
using DotNotes.Notes.Files;

namespace DotNotes.Index;

/// <summary>One note, reduced to the term counts a score is computed from.</summary>
public sealed class IndexedNote
{
	private IndexedNote(NoteHeading heading, string body, Dictionary<string, int>[] counts, int[] lengths)
	{
		Heading = heading;
		Body = body;
		Counts = counts;
		Lengths = lengths;
	}

	/// <summary>Everything a hit reports, which is everything except the prose.</summary>
	public NoteHeading Heading { get; }

	/// <summary>Kept for the extract a hit carries, which is a window of the real text.</summary>
	public string Body { get; }

	/// <summary>Term counts per field, indexed by <see cref="NoteField"/>.</summary>
	public Dictionary<string, int>[] Counts { get; }

	/// <summary>How many terms each field holds, which is what length normalisation divides by.</summary>
	public int[] Lengths { get; }

	/// <summary>How many notes link to this one, filled once the whole store is read.</summary>
	public int InboundLinks { get; set; }

	/// <summary>The targets this note links to, for the graph pass and for reporting dangling links.</summary>
	public IReadOnlyList<Wikilink> Links { get; private init; } = [];

	/// <summary>
	/// A note, read into fields.
	/// <para>
	/// The gist field takes the indexer's line where there is one and the author's description
	/// otherwise, rather than being empty on an unenriched note. A note's one-line summary is worth
	/// the same to a search whoever wrote it.
	/// </para>
	/// </summary>
	public static IndexedNote Of(Note note)
	{
		var counts = new Dictionary<string, int>[NoteFields.Count];
		var lengths = new int[NoteFields.Count];
		var matter = note.Frontmatter;

		Fill(counts, lengths, NoteField.Titles, [note.Heading.Name, .. matter.Sequence("dn-aliases")]);
		Fill(counts, lengths, NoteField.Asks, matter.Sequence("dn-asks"));
		Fill(counts, lengths, NoteField.Gist, [note.Heading.Gist ?? note.Heading.Description]);
		Fill(counts, lengths, NoteField.Topics,
			[.. note.Heading.Tags, .. matter.Sequence("dn-topics"), .. matter.Sequence("dn-entities")]);
		Fill(counts, lengths, NoteField.Headings, Headings(note.Body));
		Fill(counts, lengths, NoteField.Body, [note.Body]);

		return new IndexedNote(note.Heading, note.Body, counts, lengths) { Links = note.Links };
	}

	/// <summary>The markdown headings in a body, which are the structure its author chose.</summary>
	private static IReadOnlyList<string> Headings(string body) =>
		[.. body.Split('\n')
			.Select(line => line.TrimStart())
			.Where(line => line.StartsWith('#'))
			.Select(line => line.TrimStart('#').Trim())];

	private static void Fill(
		Dictionary<string, int>[] counts,
		int[] lengths,
		NoteField field,
		IReadOnlyList<string> values)
	{
		var index = (int)field;
		var terms = counts[index] = new Dictionary<string, int>(StringComparer.Ordinal);
		var length = 0;

		foreach (var value in values)
		{
			foreach (var term in Tokenizer.Of(value))
			{
				terms[term] = terms.GetValueOrDefault(term) + 1;
				length++;
			}
		}

		lengths[index] = length;
	}
}
