namespace DotNotes.Notes.Repositories;

/// <summary>
/// Whether two paths that differ only in case name the same directory, which is a question about
/// the platform rather than about the paths.
/// <para>
/// This is the whole of the drive-letter bug. Claude Code keys its memory on a path-encoded working
/// directory, and the encoding is case-sensitive where the filesystem is not, so
/// <c>D:\Contoso\Platform</c> and <c>d:\Contoso\Platform</c> are two stores for one repository and neither
/// can see the other's notes. Every path this server keys on is folded here first.
/// </para>
/// <para>
/// Windows is the only case-insensitive platform this ships for. macOS is deliberately not
/// considered: its default is case-insensitive and configurable, so guessing would be worse than
/// the honest note that this is the one place to revisit if it is ever targeted.
/// </para>
/// </summary>
public static class PathCasing
{
	/// <summary>Whether the filesystem this is running on treats path case as insignificant.</summary>
	public static bool IsInsensitive { get; } = OperatingSystem.IsWindows();

	/// <summary>The comparer for keying anything by path.</summary>
	public static StringComparer Comparer { get; } =
		IsInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	/// <summary>
	/// A path reduced to the form every path naming the same directory shares, for comparing and
	/// hashing. Not for display, and not for opening anything: on Windows the result is lowercased.
	/// </summary>
	public static string Fold(string path) => IsInsensitive ? path.ToLowerInvariant() : path;
}
