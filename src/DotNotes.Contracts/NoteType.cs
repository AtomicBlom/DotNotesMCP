namespace DotNotes.Contracts;

/// <summary>
/// What kind of thing a note records.
/// <para>
/// The four names Claude Code's own memory already uses, kept rather than improved on. The corpus
/// this replaces is written in them, the person reading a vault already recognises them, and a
/// better taxonomy that nothing is filed under is worth less than an adequate one that everything
/// is.
/// </para>
/// </summary>
public enum NoteType
{
	/// <summary>Something that is generally true of the work, or of the code.</summary>
	Project,

	/// <summary>Who the user is: their role, their tools, what they know.</summary>
	User,

	/// <summary>Guidance the user gave about how to work, including a correction.</summary>
	Feedback,

	/// <summary>A pointer to something outside: a URL, a dashboard, a ticket.</summary>
	Reference,
}
