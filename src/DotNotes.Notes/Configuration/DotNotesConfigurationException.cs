namespace DotNotes.Notes.Configuration;

/// <summary>
/// A configuration file that is there and cannot be understood.
/// <para>
/// Its own type, because the message a caller needs is the same every time and is about a file
/// rather than about a note: which file, and what the parser objected to. Everything else this
/// server refuses is about the request.
/// </para>
/// </summary>
public sealed class DotNotesConfigurationException(string path, Exception cause)
	: Exception($"{path} cannot be read: {cause.Message}", cause)
{
	/// <summary>The file to go and fix.</summary>
	public string Path { get; } = path;
}
