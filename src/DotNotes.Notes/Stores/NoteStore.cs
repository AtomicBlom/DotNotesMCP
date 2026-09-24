using DotNotes.Contracts;

namespace DotNotes.Notes.Stores;

/// <summary>
/// One store, and whether it can be written to.
/// <para>
/// A store that cannot be reached carries the reason and the fix rather than being absent or empty.
/// An empty answer is indistinguishable from a store with nothing in it, and a caller who gets one
/// goes back to reading files and does not come back -- so every way this can be unavailable says
/// so out loud, naming the path or the file to create.
/// </para>
/// </summary>
public sealed record NoteStore
{
	/// <summary>Which of the two this is.</summary>
	public required NoteScope Scope { get; init; }

	/// <summary>
	/// The directory notes live in. Present even when the store is unavailable, because the path is
	/// most of what the reader needs in order to fix it.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>Why it cannot be used, in a sentence naming the fix. Null when it can.</summary>
	public string? Unavailable { get; init; }

	/// <summary>Whether a note can be written here.</summary>
	public bool IsAvailable => Unavailable is null;

	/// <summary>Whether a file is inside this store, rather than in another one read alongside it.</summary>
	public bool Holds(string file) =>
		Path.Length > 0
			&& file.StartsWith(Path + System.IO.Path.DirectorySeparatorChar, Repositories.PathCasing.IsInsensitive
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal);

	/// <summary>A store that is ready, creating its directory if this is the first note.</summary>
	public static NoteStore Available(NoteScope scope, string path) => new() { Scope = scope, Path = path };

	/// <summary>A store that is not there, and what to do about it.</summary>
	public static NoteStore Refused(NoteScope scope, string path, string because) =>
		new() { Scope = scope, Path = path, Unavailable = because };

	/// <summary>
	/// The directory, created if need be. Called at the point of writing rather than at resolution,
	/// so merely asking what the stores are never creates one.
	/// </summary>
	/// <exception cref="InvalidOperationException">The store is unavailable.</exception>
	public string Ensure()
	{
		if (Unavailable is { } because) throw new InvalidOperationException(because);

		Directory.CreateDirectory(Path);

		return Path;
	}
}
