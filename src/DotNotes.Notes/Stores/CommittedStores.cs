using System.Collections.Concurrent;

using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Repositories;

namespace DotNotes.Notes.Stores;

/// <summary>
/// Where a checkout's committed notes are: every folder holding an <c>index.md</c> that DotNotes
/// generated.
/// <para>
/// Found rather than configured, because the index marks itself and moves with its folder. A person
/// who moves the notes moves the beacon without knowing it is one, where a path in a config file is a
/// second record of the same fact and the one that goes stale. Found without walking the tree: the
/// tracked paths come from git's index, and the one untracked store there can be -- the one just made
/// by <c>--init</c>, before its first commit -- is at the default path, which is checked by name. See
/// <c>docs/decisions/the-committed-store-is-found-rather-than-configured.md</c>.
/// </para>
/// </summary>
public static class CommittedStores
{
	/// <summary>
	/// The head of a file long enough to hold the frontmatter and the marker the generator writes
	/// first, so recognising an index never reads a long one whole.
	/// </summary>
	private const int HeadLength = 512;

	/// <summary>
	/// Whether each file carried the marker, kept by its write time. A monorepo with a documentation
	/// site tracks hundreds of <c>index.md</c> files; opening every one on every call is what this
	/// saves, and a changed file is looked at again.
	/// </summary>
	private static readonly ConcurrentDictionary<string, (long Written, bool Marked)> Marked = new(PathCasing.Comparer);

	/// <summary>The store's folder, relative to the checkout, when nothing says otherwise.</summary>
	public static string DefaultFolder { get; } = Path.Combine(RepositoryConfigFile.DirectoryName, "notes");

	/// <summary>Every folder in this checkout holding a generated index, canonical and sorted.</summary>
	public static IReadOnlyList<string> Found(RepositoryIdentity identity)
	{
		if (identity.Worktree is not { Length: > 0 } worktree) return [];

		var candidates = new List<string> { Path.Combine(worktree, DefaultFolder, NoteIndexFile.FileName) };

		if (identity.GitDirectory is { Length: > 0 } git)
		{
			foreach (var tracked in GitIndex.Tracked(git, NoteIndexFile.FileName))
			{
				candidates.Add(Path.Combine([worktree, .. tracked.Split('/')]));
			}
		}

		return [.. candidates
			.Distinct(PathCasing.Comparer)
			.Where(IsGenerated)
			.Select(index => CanonicalPath.Of(Path.GetDirectoryName(index)!))
			.Distinct(PathCasing.Comparer)
			.Order(StringComparer.Ordinal)];
	}

	/// <summary>The store the checkout's config names, or null where it has none.</summary>
	public static string? Configured(RepositoryIdentity identity) =>
		identity.Config is { Path: { Length: > 0 } file } config
			? CanonicalPath.Of(Path.Combine(Path.GetDirectoryName(file)!, config.Notes))
			: null;

	/// <summary>
	/// Which store a write from <paramref name="directory"/> goes to, or null where there are several
	/// and nothing chooses between them.
	/// <para>
	/// The nearest found store enclosing the directory, because in a monorepo that is the project the
	/// session is working in. Then the configured one if it has notes. Then the only one found, which
	/// beats a configured folder with nothing in it: that is a store somebody moved without editing
	/// the config, and writing to the path the config still names would start a second store beside
	/// it. Then the configured one, for the first note after opting in.
	/// </para>
	/// </summary>
	/// <param name="found">The stores holding a generated index.</param>
	/// <param name="directory">Where the call came from.</param>
	/// <param name="configured">The store the config names, or null.</param>
	public static string? WriteTarget(IReadOnlyList<string> found, string directory, string? configured)
	{
		var enclosing = found
			.Where(store => Encloses(store, directory))
			.OrderByDescending(store => store.Length)
			.FirstOrDefault();

		if (enclosing is not null) return enclosing;

		if (configured is not null && found.Contains(configured, PathCasing.Comparer)) return configured;

		if (found.Count == 1) return found[0];

		return found.Count == 0 ? configured : null;
	}

	/// <summary>Whether a directory is a store or inside one.</summary>
	private static bool Encloses(string store, string directory)
	{
		var comparison = PathCasing.IsInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		return directory.Equals(store, comparison)
			|| directory.StartsWith(store + Path.DirectorySeparatorChar, comparison);
	}

	/// <summary>Whether a file is an index DotNotes generated, judged by its head.</summary>
	private static bool IsGenerated(string path)
	{
		try
		{
			var info = new FileInfo(path);
			if (!info.Exists) return false;

			var written = info.LastWriteTimeUtc.Ticks;

			if (Marked.TryGetValue(path, out var known) && known.Written == written) return known.Marked;

			using var reader = new StreamReader(path);
			var head = new char[HeadLength];
			var read = reader.ReadBlock(head, 0, head.Length);
			var marked = NoteIndexFile.IsGenerated(new string(head, 0, read));

			Marked[path] = (written, marked);

			return marked;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
