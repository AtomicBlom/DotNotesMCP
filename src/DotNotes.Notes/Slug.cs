using System.Text;

namespace DotNotes.Notes;

/// <summary>
/// A name reduced to something that is a directory name on every platform, a wikilink target in
/// Obsidian, and the same string whichever of those wrote it.
/// </summary>
public static class Slug
{
	/// <summary>The longest a slug may be, which is a directory name inside a path that is already long.</summary>
	private const int Limit = 64;

	/// <summary>
	/// Names Windows will not give a file, whatever the extension. A repository legitimately called
	/// <c>aux</c> would otherwise resolve to a name that cannot be created, and the failure arrives
	/// at the first write rather than at the resolution that chose it.
	/// </summary>
	private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
	{
		"con", "prn", "aux", "nul",
		"com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
		"lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
	};

	/// <summary>
	/// Lower case, digits and single hyphens. An empty or entirely unusable name becomes
	/// <c>unnamed</c> rather than an empty string, because a caller cannot act on a name that is not
	/// there and a path built from one lands somewhere unintended.
	/// </summary>
	public static string Of(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return "unnamed";

		var builder = new StringBuilder(name.Length);

		foreach (var character in name)
		{
			if (char.IsAsciiLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
				continue;
			}

			// One hyphen for any run of anything else, so "Rose MCP" and "Rose_MCP" agree and
			// "a  b" does not become "a--b".
			if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
		}

		var slug = builder.ToString().Trim('-');

		if (slug.Length > Limit) slug = slug[..Limit].TrimEnd('-');

		if (slug.Length == 0) return "unnamed";

		return Reserved.Contains(slug) ? slug + "-repo" : slug;
	}
}
