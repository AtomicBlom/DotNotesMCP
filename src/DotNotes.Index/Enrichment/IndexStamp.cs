using System.Security.Cryptography;
using System.Text;

using DotNotes.Notes.Files;

namespace DotNotes.Index.Enrichment;

/// <summary>
/// What a note says about its own enrichment: the shape it was written in, the instructions it was
/// written to, and the text it was written about.
/// <para>
/// In the note rather than in a journal, which does not contradict the rule that nothing volatile
/// goes in a note -- every part of this changes exactly when the enrichment stops being true, which
/// is when the file is being rewritten anyway. What it buys is that a machine which has never
/// indexed this store can tell fresh from stale by reading the notes, so a second machine, or a
/// fresh clone, does not re-enrich a corpus somebody already paid for.
/// </para>
/// <para>
/// One opaque key rather than three readable ones, because a person editing properties in Obsidian
/// has no use for any of it and three lines of machine bookkeeping in the pane is three too many.
/// </para>
/// </summary>
public readonly record struct IndexStamp(int Schema, string Prompt, string Source)
{
	/// <summary>The frontmatter key it lives under.</summary>
	public const string Key = "dn-index";

	/// <summary>
	/// The shape of an enrichment record. Bumped when a field is added, removed or reinterpreted,
	/// which stales every note at once -- correct, and the reason the shape is worth settling early.
	/// </summary>
	public const int CurrentSchema = 2;

	/// <summary>As it appears in the note.</summary>
	public override string ToString() => $"{Schema}/{Prompt}/{Source}";

	/// <summary>The stamp a note carries, or null where it carries none or nonsense.</summary>
	public static IndexStamp? Of(NoteFrontmatter frontmatter)
	{
		if (frontmatter.Scalar(Key) is not { Length: > 0 } value) return null;

		var parts = value.Split('/');

		if (parts.Length != 3 || !int.TryParse(parts[0], out var schema)) return null;

		return new IndexStamp(schema, parts[1], parts[2]);
	}

	/// <summary>The stamp a note should carry, given the instructions and its own text.</summary>
	public static IndexStamp For(string promptHash, string content) =>
		new(CurrentSchema, promptHash, Short(NoteFile.SourceHash(content)));

	/// <summary>
	/// Eight hex characters of the instruction text. Short because it is read by nobody and stored
	/// in every note; eight is far more than enough to notice that the brief changed.
	/// </summary>
	public static string HashOf(string prompt) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)).AsSpan(0, 4));

	/// <summary>Twelve characters of a content hash, which is plenty to tell two versions apart.</summary>
	private static string Short(string hash) => hash[..12];
}
