namespace DotNotes.Notes.Repositories;

/// <summary>
/// An origin remote folded to the form every spelling of the same remote shares.
/// <para>
/// This is what makes two clones of one repository -- on two machines, at two paths, one cloned over
/// ssh and one over https -- file their notes under one name. Folding wrong is not cosmetic: it
/// splits a repository's notes exactly the way keying on the working directory already does.
/// </para>
/// <para>
/// No provider is special-cased, and that is deliberate. Azure DevOps serves one repository as both
/// <c>dev.azure.com/org/project/_git/name</c> and <c>ssh.dev.azure.com:v3/org/project/name</c>,
/// which differ in host and in path depth; folding those together means encoding one provider's URL
/// scheme here and keeping it correct as it changes. The answer for a remote that will not fold is
/// the step above this one in the chain -- a name in <c>.dotnotes/dotnotes.json</c>, chosen once and
/// committed.
/// </para>
/// </summary>
public static class RemoteName
{
	/// <summary>
	/// The identity of a remote as <c>host/path</c>, lowercased and without credentials, port or
	/// <c>.git</c> suffix -- or null where the remote names no shared identity.
	/// </summary>
	public static string? Normalise(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return null;

		var trimmed = url.Trim();

		// A path is somewhere on one machine, so two clones from it are not two clones of a shared
		// thing. Null here falls through to the next step of the chain rather than inventing an
		// identity that only this machine could agree with.
		if (IsLocalPath(trimmed)) return null;

		var (host, path) = Split(trimmed);

		if (host is not { Length: > 0 } || path is not { Length: > 0 }) return null;

		return $"{host.ToLowerInvariant()}/{path.ToLowerInvariant()}";
	}

	/// <summary>The last segment of a folded remote, which is what a repository is usually called.</summary>
	public static string? LastSegment(string? normalised)
	{
		if (normalised is not { Length: > 0 }) return null;

		var slash = normalised.LastIndexOf('/');

		return slash >= 0 && slash < normalised.Length - 1 ? normalised[(slash + 1)..] : null;
	}

	/// <summary>
	/// Whether a remote names a place on a filesystem rather than a host. A Windows drive is the
	/// case that matters, because <c>D:\mirrors\x.git</c> reads exactly like the <c>host:path</c>
	/// form an ssh remote uses, and a one-letter host is the tell.
	/// </summary>
	private static bool IsLocalPath(string url)
	{
		if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
		if (url.StartsWith('/') || url.StartsWith(@"\\")) return true;

		return url.Length >= 2 && url[1] == ':' && char.IsAsciiLetter(url[0]);
	}

	/// <summary>
	/// The host and path of a remote in either of the two shapes git accepts: a URL with a scheme,
	/// and the scp-like <c>[user@]host:path</c>.
	/// </summary>
	private static (string? Host, string? Path) Split(string url)
	{
		if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
		{
			return (uri.Host, Clean(uri.AbsolutePath));
		}

		var colon = url.IndexOf(':', StringComparison.Ordinal);
		if (colon <= 0) return (null, null);

		var authority = url[..colon];
		var at = authority.LastIndexOf('@');
		var host = at >= 0 ? authority[(at + 1)..] : authority;

		return (host, Clean(url[(colon + 1)..]));
	}

	/// <summary>A remote's path with its separators, its wrapping slashes and its <c>.git</c> removed.</summary>
	private static string Clean(string path)
	{
		var cleaned = path.Replace('\\', '/').Trim('/');

		if (cleaned.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
		{
			cleaned = cleaned[..^4].TrimEnd('/');
		}

		return cleaned;
	}
}
