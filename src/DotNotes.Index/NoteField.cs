namespace DotNotes.Index;

/// <summary>
/// The parts of a note a query is matched against, and what each is worth.
/// <para>
/// Every field is weighted from the first note indexed, including the ones only the indexing mode
/// fills. An empty field contributes nothing to a score, so a note is findable the moment it is
/// saved and enrichment only makes some entries richer -- which means there is no second retrieval
/// path for unenriched notes, and so no second path to get wrong.
/// </para>
/// </summary>
public enum NoteField
{
	/// <summary>The note's name and any aliases. A query naming the note is the strongest signal there is.</summary>
	Titles,

	/// <summary>
	/// The questions the indexing mode wrote. Second only to the title, and the bet the whole design
	/// makes: a question authored in the form a query arrives in is what bridges the gap between the
	/// words a note used and the words somebody later types.
	/// </summary>
	Asks,

	/// <summary>The one-line summary: the author's description, or the indexer's gist over it.</summary>
	Gist,

	/// <summary>Tags and topics. A controlled vocabulary breaks a tie; it must not decide a ranking.</summary>
	Topics,

	/// <summary>The note's own headings. Real structure, and often generic.</summary>
	Headings,

	/// <summary>
	/// The prose. Deliberately not weighted below one: exact rare identifiers appear only here, and
	/// they are what a lexical index is best at. Weighting the body down to chase something more
	/// semantic is how an index is made worse than the plain search it replaced.
	/// </summary>
	Body,
}

/// <summary>What each field contributes, and the constants the scoring uses.</summary>
public static class NoteFields
{
	/// <summary>Every field, in declaration order, for iterating without allocating.</summary>
	public static readonly NoteField[] All = Enum.GetValues<NoteField>();

	/// <summary>How many there are, which is the width of every per-field array.</summary>
	public static readonly int Count = All.Length;

	/// <summary>
	/// Saturation. The standard 1.2: a term appearing ten times is worth more than once and nowhere
	/// near ten times as much, which is what stops a note that repeats a word from beating one that
	/// is about it.
	/// </summary>
	public const double K1 = 1.2;

	/// <summary>
	/// How much length is normalised away. The standard 0.75: a long note is penalised for being
	/// long, but not so much that a thorough note loses to a stub that happens to say the word.
	/// </summary>
	public const double B = 0.75;

	/// <summary>What a match in each field is worth, indexed by <see cref="NoteField"/>.</summary>
	public static double Weight(NoteField field) => field switch
	{
		NoteField.Titles => 8.0,
		NoteField.Asks => 5.0,
		NoteField.Gist => 4.0,
		NoteField.Topics => 3.0,
		NoteField.Headings => 2.0,
		_ => 1.0,
	};
}
