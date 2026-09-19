using System.Security.Cryptography;
using System.Text;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// The one spelling of a path that this server keys on, and the short hash of it.
/// <para>
/// A caller may give a relative path, a path with a trailing separator, an extended-length path, or
/// a drive letter in either case, and all of them name one directory. Reducing them here means every
/// later comparison is string equality rather than a filesystem question asked again.
/// </para>
/// </summary>
public static class CanonicalPath
{
	private const string ExtendedLengthPrefix = @"\\?\";

	/// <summary>
	/// The absolute, separator-normalised path with no trailing separator, safe to show a reader.
	/// <para>
	/// The drive letter is upper-cased, which is not folding: a drive letter is case-insensitive on
	/// Windows whatever else the filesystem does, so this settles the one component that would
	/// otherwise make an identical path print two ways. Everything after it keeps the case on disk,
	/// because that is what a person recognises.
	/// </para>
	/// </summary>
	public static string Of(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return string.Empty;

		var trimmed = path.Trim();

		if (trimmed.StartsWith(ExtendedLengthPrefix, StringComparison.Ordinal))
		{
			trimmed = trimmed[ExtendedLengthPrefix.Length..];
		}

		var full = Path.GetFullPath(trimmed);
		var root = Path.GetPathRoot(full) ?? string.Empty;

		// A root is the one path allowed to end in a separator: C:\ is a directory and C: is a drive
		// -relative reference to somewhere else entirely.
		if (full.Length > root.Length)
		{
			full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		var drive = full.Length >= 2 && full[1] == ':';

		return drive ? char.ToUpperInvariant(full[0]) + full[1..] : full;
	}

	/// <summary>
	/// Eight hex characters of SHA-256 over the folded path: enough to separate two directories that
	/// share a name, short enough to live in one.
	/// </summary>
	public static string Hash(string path)
	{
		var folded = PathCasing.Fold(Of(path));
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(folded));

		return Convert.ToHexStringLower(digest.AsSpan(0, 4));
	}
}
