using System.Text.Json;
using System.Text.Json.Serialization;

using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Repositories;

namespace DotNotes.Notes.Stores;

/// <summary>
/// What each machine store has been seen as, at <c>%LOCALAPPDATA%/BinaryVibrance/DotNotes/repositories.json</c>.
/// <para>
/// The naming chain is right about a repository as it is at the moment it is asked, and a person
/// changes what it would answer by doing something ordinary: adding an origin, moving the folder,
/// renaming the repository on the host. Each of those gives the repository a new key, and a key with
/// no store reads exactly like a repository nobody has written notes for. This file is how the old
/// store is found again: for every store, the common directories, remotes and root commits of the
/// repositories that resolved to it.
/// </para>
/// <para>
/// Consulted, never trusted. The key and the opt-in are resolved fresh on every call, and every
/// store this names is checked on disk before it is used, so deleting the file loses the ability to
/// notice a move and nothing else. Beside the settings and the locks rather than in the machine
/// store, because a vault may sync and the paths in here mean something only on this machine.
/// </para>
/// </summary>
public sealed record RepositoryEvidence
{
	/// <summary>The file's own name, beside the settings.</summary>
	public const string FileName = "repositories.json";

	/// <summary>
	/// What has been seen of each store, by the store's folded path. Settable for the same reason as
	/// <see cref="RepositoryConfigFile"/>: the source generator otherwise overwrites a default with
	/// null for a key the file omits.
	/// </summary>
	public Dictionary<string, StoreSighting> Stores { get; set; } = [];

	/// <summary>Why the file could not be read, or null. A file that cannot be read is never written over.</summary>
	[JsonIgnore]
	public string? Unreadable { get; set; }

	/// <summary>The file, which need not exist.</summary>
	public static string PathFor(string? localAppData = null) =>
		Path.Combine(MachineSettingsFile.DirectoryFor(localAppData), FileName);

	/// <summary>
	/// What has been recorded, or nothing.
	/// <para>
	/// Never throws. This is a record of what was seen rather than a choice somebody made, so a file
	/// that cannot be parsed is reported by a notice on every answer and otherwise treated as empty:
	/// refusing to serve notes over a damaged cache would take the whole server down for a feature
	/// whose failure mode is no worse than not having it.
	/// </para>
	/// </summary>
	public static RepositoryEvidence Read(string? localAppData = null)
	{
		var path = PathFor(localAppData);

		try
		{
			if (!File.Exists(path)) return new RepositoryEvidence();

			var parsed = JsonSerializer.Deserialize(File.ReadAllText(path), RepositoryEvidenceJson.Default.RepositoryEvidence);

			return Folded(parsed ?? new RepositoryEvidence());
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return new RepositoryEvidence
			{
				Unreadable = $"{path} cannot be read ({exception.Message}), so notes filed under an earlier "
					+ "name for this repository are not being looked for. Delete it to start again.",
			};
		}
	}

	/// <summary>
	/// Changes the file under its lock, re-reading it first so two sessions recording at once both
	/// land. Written only when the change altered something, and never over a file that could not be
	/// read.
	/// </summary>
	/// <returns>Whether the file was written.</returns>
	public static bool Update(Func<RepositoryEvidence, bool> change, TimeSpan timeout, string? localAppData = null)
	{
		var path = PathFor(localAppData);

		using var held = StoreLock.Take(path, timeout, localAppData);

		var current = Read(localAppData);

		if (current.Unreadable is not null || !change(current)) return false;

		return NoteFile.Write(path, current.Serialized());
	}

	/// <summary>Adds what one call saw to the store it used: the repository, its remote and its roots.</summary>
	/// <returns>Whether anything was new.</returns>
	public bool Saw(string store, RepositoryIdentity identity, IReadOnlyList<string> roots)
	{
		if (identity.CommonDirectory is not { Length: > 0 } common) return false;

		var sighting = Sighting(store);
		var changed = Add(sighting.Repositories, common, PathCasing.Comparer);

		if (identity.Remote is { Length: > 0 } remote) changed |= Add(sighting.Remotes, remote, StringComparer.Ordinal);

		foreach (var root in roots) changed |= Add(sighting.Roots, root, StringComparer.Ordinal);

		return changed;
	}

	/// <summary>
	/// The stores this evidence attributes to a repository other than the one it resolved to: under
	/// the same machine root, still on disk, not dismissed, and sharing either its common directory
	/// or one of its roots.
	/// </summary>
	/// <param name="resolved">The store the naming chain chose, which is never its own candidate.</param>
	/// <param name="machineRoot">The root the stores are folders of. A store under another root is not reachable from this configuration.</param>
	/// <param name="identity">The repository asking.</param>
	/// <param name="roots">Its roots, from <see cref="RootCommits.Of"/>.</param>
	/// <param name="shortName">
	/// The folder the repository's name alone would be, where that is not its key: <c>rosemcp</c>
	/// beside <c>atomicblom-rosemcp</c>. A store there is offered whether or not anything was recorded
	/// of it, because a remote-named repository's notes are under its short name wherever they were
	/// written by a server that keyed on the last segment.
	/// </param>
	public IReadOnlyList<MoveCandidate> CandidatesFor(
		string resolved,
		string machineRoot,
		RepositoryIdentity identity,
		IReadOnlyList<string> roots,
		string? shortName = null)
	{
		if (identity.CommonDirectory is not { Length: > 0 } common) return [];

		var own = PathCasing.Fold(resolved);
		var root = PathCasing.Fold(machineRoot);
		var dismissed = Stores.TryGetValue(own, out var mine) ? mine.Dismissed : [];
		var candidates = new List<MoveCandidate>();

		foreach (var (key, sighting) in Stores)
		{
			if (key == own || PathCasing.Fold(Path.GetDirectoryName(key) ?? string.Empty) != root) continue;
			if (dismissed.Contains(key, PathCasing.Comparer)) continue;

			var byDirectory = sighting.Repositories.Contains(common, PathCasing.Comparer);
			var byRoots = sighting.Roots.Intersect(roots, StringComparer.Ordinal).Any();

			if (!byDirectory && !byRoots) continue;

			var path = Path.Combine(machineRoot, Path.GetFileName(key));
			if (!Directory.Exists(path)) continue;

			candidates.Add(new MoveCandidate { Path = path, Unambiguous = Unowned(sighting, common) });
		}

		if (shortName is { Length: > 0 } && Path.Combine(machineRoot, shortName) is var named
			&& Directory.Exists(named)
			&& PathCasing.Fold(named) != own
			&& !dismissed.Contains(PathCasing.Fold(named), PathCasing.Comparer)
			&& !candidates.Any(candidate => PathCasing.Comparer.Equals(candidate.Path, named)))
		{
			var sighting = Stores.GetValueOrDefault(PathCasing.Fold(named));

			candidates.Add(new MoveCandidate { Path = named, Unambiguous = sighting is null || Unowned(sighting, common) });
		}

		return [.. candidates.OrderBy(candidate => candidate.Path, StringComparer.Ordinal)];
	}

	/// <summary>
	/// Whether nothing but this repository can own a store: every other checkout it was seen with is
	/// gone. A fork's upstream shares its roots and is still on disk, and so are both halves of two
	/// repositories that once shared a short name; each of those is a store to read from, never one to
	/// write into.
	/// </summary>
	private static bool Unowned(StoreSighting sighting, string common) =>
		sighting.Repositories.All(repository =>
			PathCasing.Comparer.Equals(repository, common) || !Directory.Exists(repository));

	/// <summary>Folds what one store has seen into another, which is what an adoption does to the evidence.</summary>
	public void Merge(string from, string into)
	{
		var key = PathCasing.Fold(CanonicalPath.Of(from));
		if (!Stores.Remove(key, out var moved)) return;

		var target = Sighting(into);

		foreach (var repository in moved.Repositories) Add(target.Repositories, repository, PathCasing.Comparer);
		foreach (var remote in moved.Remotes) Add(target.Remotes, remote, StringComparer.Ordinal);
		foreach (var root in moved.Roots) Add(target.Roots, root, StringComparer.Ordinal);
	}

	/// <summary>Records that a store is not the given one's, so it stops being offered.</summary>
	/// <returns>Whether it was not already dismissed.</returns>
	public bool Dismiss(string store, string candidate) =>
		Add(Sighting(store).Dismissed, PathCasing.Fold(CanonicalPath.Of(candidate)), PathCasing.Comparer);

	/// <summary>The sighting for a store, created empty if it has none.</summary>
	private StoreSighting Sighting(string store)
	{
		var key = PathCasing.Fold(CanonicalPath.Of(store));

		if (!Stores.TryGetValue(key, out var sighting))
		{
			sighting = new StoreSighting();
			Stores[key] = sighting;
		}

		return sighting;
	}

	/// <summary>
	/// The file's text, sorted throughout. Stable for stable content, so recording nothing new writes
	/// nothing -- the same rule as every other file here, and the one that keeps this off a sync
	/// service's revision history.
	/// </summary>
	private string Serialized()
	{
		var sorted = new RepositoryEvidence
		{
			Stores = Stores.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(
				pair => pair.Key,
				pair => new StoreSighting
				{
					Repositories = [.. pair.Value.Repositories.Order(StringComparer.Ordinal)],
					Remotes = [.. pair.Value.Remotes.Order(StringComparer.Ordinal)],
					Roots = [.. pair.Value.Roots.Order(StringComparer.Ordinal)],
					Dismissed = [.. pair.Value.Dismissed.Order(StringComparer.Ordinal)],
				}),
		};

		return JsonSerializer.Serialize(sorted, RepositoryEvidenceJson.Default.RepositoryEvidence) + "\n";
	}

	/// <summary>
	/// Keys folded on the way in, so a file somebody edited by hand with a differently-cased drive
	/// letter still matches.
	/// </summary>
	private static RepositoryEvidence Folded(RepositoryEvidence read) => read with
	{
		Stores = read.Stores
			.GroupBy(pair => PathCasing.Fold(pair.Key))
			.ToDictionary(group => group.Key, group => group.First().Value),
	};

	private static bool Add(List<string> list, string value, StringComparer comparer)
	{
		if (list.Contains(value, comparer)) return false;

		list.Add(value);

		return true;
	}
}

/// <summary>What the repositories that resolved to one store looked like.</summary>
public sealed class StoreSighting
{
	/// <summary>Their common directories: the same one for every worktree of a repository.</summary>
	public List<string> Repositories { get; set; } = [];

	/// <summary>Their remotes, folded, for a person reading why a store was offered.</summary>
	public List<string> Remotes { get; set; } = [];

	/// <summary>Their root commits.</summary>
	public List<string> Roots { get; set; } = [];

	/// <summary>Stores a person has said are not this one's, by folded path.</summary>
	public List<string> Dismissed { get; set; } = [];
}

/// <summary>A store the evidence attributes to this repository under another key.</summary>
public sealed record MoveCandidate
{
	/// <summary>The store, as it is on disk under the machine root.</summary>
	public required string Path { get; init; }

	/// <summary>
	/// Whether nothing else can own it: it was seen with this very git directory, or every checkout it
	/// was seen with is gone. Only an unambiguous candidate is ever written to.
	/// </summary>
	public required bool Unambiguous { get; init; }
}

/// <summary>The evidence file's shape, generated rather than discovered by reflection.</summary>
[JsonSourceGenerationOptions(
	JsonSerializerDefaults.Web,
	ReadCommentHandling = JsonCommentHandling.Skip,
	AllowTrailingCommas = true,
	WriteIndented = true)]
[JsonSerializable(typeof(RepositoryEvidence))]
internal sealed partial class RepositoryEvidenceJson : JsonSerializerContext;
