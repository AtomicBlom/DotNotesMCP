namespace DotNotes.Notes.Repositories;

/// <summary>
/// The one thing this server wants out of git's config: the URL of the origin remote.
/// <para>
/// Enough INI to find one key in one section, rather than a parser for a format with includes,
/// conditional includes and three quoting rules. Anything it cannot make sense of is no remote,
/// which falls through to the next step of the naming chain -- so being wrong here costs a less
/// portable name, never a wrong one.
/// </para>
/// </summary>
public static class GitConfigFile
{
	/// <summary>The origin remote's URL, or null where the file, the section or the key is absent.</summary>
	public static string? OriginUrl(string? commonDirectory)
	{
		if (commonDirectory is not { Length: > 0 }) return null;

		var path = Path.Combine(commonDirectory, "config");

		string[] lines;

		try
		{
			if (!File.Exists(path)) return null;

			lines = File.ReadAllLines(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}

		var inOrigin = false;

		foreach (var raw in lines)
		{
			var line = Strip(raw);
			if (line.Length == 0) continue;

			if (line[0] == '[')
			{
				inOrigin = IsOriginSection(line);
				continue;
			}

			if (!inOrigin) continue;

			var equals = line.IndexOf('=', StringComparison.Ordinal);
			if (equals <= 0) continue;

			if (!line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) continue;

			var url = line[(equals + 1)..].Trim().Trim('"');

			// The first url wins, as it does for git: a later one in the same section is a
			// pushurl or a hand-edit, and either way the fetch URL is the identity.
			if (url.Length > 0) return url;
		}

		return null;
	}

	/// <summary>
	/// A section header naming the origin remote, in either spelling git writes:
	/// <c>[remote "origin"]</c>, and the subsection-less <c>[remote.origin]</c> some tools emit.
	/// </summary>
	private static bool IsOriginSection(string line)
	{
		var inner = line.Trim('[', ']').Trim();

		if (inner.Equals("remote.origin", StringComparison.OrdinalIgnoreCase)) return true;

		if (!inner.StartsWith("remote", StringComparison.OrdinalIgnoreCase)) return false;

		return inner[6..].Trim().Trim('"').Equals("origin", StringComparison.Ordinal);
	}

	/// <summary>A line without its comment or its surrounding space.</summary>
	private static string Strip(string line)
	{
		var trimmed = line.Trim();
		var comment = trimmed.IndexOfAny([';', '#']);

		return comment >= 0 ? trimmed[..comment].Trim() : trimmed;
	}
}
