using DotNotes.Contracts;
using DotNotes.Notes.Files;
using DotNotes.Notes.Repositories;
using DotNotes.Notes.Stores;

namespace DotNotes.Server;

/// <summary>
/// The explicit step that makes a pending move: a command a person runs, never a tool.
/// <para>
/// A command because it runs once per rename, and a tool's description is paid for by every session
/// whether or not anything is ever renamed. Never automatic, because several worktrees of one
/// repository are live at once and the evidence that proposed the move can be wrong in ways only a
/// person can see. See <c>docs/decisions/a-renamed-repository-keeps-its-notes-until-the-move-is-made.md</c>.
/// </para>
/// </summary>
public sealed partial class NoteService
{
	/// <summary>
	/// Moves the stores waiting for this repository into the store its key names: a rename where
	/// there is one and nothing is there yet, a merge otherwise.
	/// <para>
	/// A merge refuses before it moves anything if two notes claim one name with different contents.
	/// A move that stopped halfway would leave notes in both places with nothing to say which was
	/// meant, and a merge that chose one would be overwriting a file the person may have edited.
	/// </para>
	/// </summary>
	/// <param name="directory">The repository, as any directory inside it.</param>
	/// <param name="only">One candidate's folder name, or null for all of them.</param>
	/// <returns>What was done, as lines for a person.</returns>
	/// <exception cref="McpRefusal">Nothing is pending, the named candidate is not one, or two notes collide.</exception>
	public IReadOnlyList<string> Adopt(string directory, string? only = null)
	{
		var stores = Stores(directory);
		var (pending, chosen) = Chosen(stores, only);
		var target = pending.Resolved;

		using (Locks([.. chosen, target]))
		{
			var lines = !Directory.Exists(target) && chosen is [var single]
				? Rename(single, target)
				: Merge(chosen, target);

			RepositoryEvidence.Update(
				evidence =>
				{
					foreach (var from in chosen) evidence.Merge(from, target);

					return true;
				},
				options.StoreLockTimeout,
				options.LocalAppData);

			Regenerate(Stores(directory), NoteScope.Machine);

			return lines;
		}
	}

	/// <summary>
	/// Records that the stores waiting for this repository are not its own, which is the answer for a
	/// fork whose upstream shares its history. Nothing on disk is touched.
	/// </summary>
	/// <exception cref="McpRefusal">Nothing is pending, or the named candidate is not one.</exception>
	public IReadOnlyList<string> Dismiss(string directory, string? only = null)
	{
		var stores = Stores(directory);
		var (pending, chosen) = Chosen(stores, only);

		RepositoryEvidence.Update(
			evidence => chosen.Aggregate(false, (changed, candidate) => evidence.Dismiss(pending.Resolved, candidate) | changed),
			options.StoreLockTimeout,
			options.LocalAppData);

		return [.. chosen.Select(candidate =>
			$"{Path.GetFileName(candidate)} is no longer offered to {Path.GetFileName(pending.Resolved)}.")];
	}

	/// <summary>
	/// Opts a checkout in to committed notes by writing an empty generated index where the notes go.
	/// A command a person runs, because committing notes into a shared repository is the repository
	/// owner's decision rather than one an agent makes on first contact.
	/// </summary>
	/// <exception cref="McpRefusal">There is no working tree, or the checkout has already opted in.</exception>
	public IReadOnlyList<string> Init(string directory)
	{
		var stores = Stores(directory);
		var identity = stores.Repository;

		if (!identity.HasWorkingTree) throw new McpRefusal(stores.Repo.Unavailable!);

		var existing = stores.Reading(StoreSelection.Repository).Where(store => store.IsAvailable).ToArray();

		if (existing.Length > 0)
		{
			throw new McpRefusal(
				$"This checkout has already opted in: its committed notes are in {string.Join(", ", existing.Select(store => store.Path))}.");
		}

		var folder = Path.Combine(identity.Worktree!, CommittedStores.DefaultFolder);
		var index = Path.Combine(folder, NoteIndexFile.FileName);

		NoteFile.Write(index, NoteIndexFile.Render(identity.Name, []));

		return [$"Created {index}. Commit it, and committed notes are shared with everyone who clones."];
	}

	/// <summary>The pending move and the candidates a call chose from it.</summary>
	/// <exception cref="McpRefusal">Nothing is pending, or the named candidate is not one.</exception>
	private static (PendingMove Pending, IReadOnlyList<string> Chosen) Chosen(NoteStores stores, string? only)
	{
		if (stores.Pending is not { } pending)
		{
			throw new McpRefusal(
				$"Nothing is waiting to move for {stores.Repository.Key}: its machine notes are at {stores.Machine.Path}.");
		}

		var chosen = pending.Candidates
			.Select(candidate => candidate.Path)
			.Where(path => only is not { Length: > 0 } || Path.GetFileName(path).Equals(only, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		if (chosen.Length == 0)
		{
			throw new McpRefusal(
				$"'{only}' is not waiting to move. These are: "
					+ string.Join(", ", pending.Candidates.Select(candidate => Path.GetFileName(candidate.Path))) + ".");
		}

		return (pending, chosen);
	}

	/// <summary>
	/// Every store's lock, taken in one order so two adoptions that overlap cannot each hold what the
	/// other is waiting for.
	/// </summary>
	private Held Locks(IEnumerable<string> paths)
	{
		var held = new Held();

		try
		{
			foreach (var path in paths.Distinct(PathCasing.Comparer).OrderBy(PathCasing.Fold, StringComparer.Ordinal))
			{
				held.Add(StoreLock.Take(path, options.StoreLockTimeout, options.LocalAppData));
			}

			return held;
		}
		catch
		{
			held.Dispose();
			throw;
		}
	}

	/// <summary>One folder renamed to the key's, which is the whole of the ordinary case.</summary>
	private static IReadOnlyList<string> Rename(string from, string to)
	{
		Directory.Move(from, to);

		return [$"Moved {from} to {to}."];
	}

	/// <summary>
	/// Every file in the candidates moved into the target, planned whole before anything moves. A file
	/// already there with the same bytes is a copy and is dropped; with different bytes it is a
	/// collision and nothing moves. The generated index is dropped too, because the target's is
	/// regenerated from what arrives.
	/// </summary>
	/// <exception cref="McpRefusal">Two files claim one path with different contents.</exception>
	private static IReadOnlyList<string> Merge(IReadOnlyList<string> candidates, string target)
	{
		var moves = new List<(string From, string To)>();
		var copies = new List<string>();
		var collisions = new List<string>();
		var claimed = new Dictionary<string, string>(PathCasing.Comparer);

		foreach (var candidate in candidates)
		{
			foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
			{
				var relative = Path.GetRelativePath(candidate, file);

				if (IsGeneratedIndex(relative, file))
				{
					copies.Add(file);
					continue;
				}

				var destination = Path.Combine(target, relative);
				var occupant = claimed.GetValueOrDefault(destination) ?? (File.Exists(destination) ? destination : null);

				if (occupant is null)
				{
					claimed[destination] = file;
					moves.Add((file, destination));
				}
				else if (Same(file, occupant))
				{
					copies.Add(file);
				}
				else
				{
					collisions.Add($"{relative}: {file} and {occupant}");
				}
			}
		}

		if (collisions.Count > 0)
		{
			throw new McpRefusal(
				"Nothing was moved, because these differ and each is somebody's note: "
					+ string.Join("; ", collisions) + ". Rename or remove one of each, then run it again.");
		}

		foreach (var (from, to) in moves)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(to)!);
			File.Move(from, to, overwrite: false);
		}

		foreach (var copy in copies) File.Delete(copy);

		foreach (var candidate in candidates) RemoveEmpty(candidate);

		var duplicates = copies.Count(copy => !Path.GetFileName(copy).Equals(NoteIndexFile.FileName, StringComparison.OrdinalIgnoreCase));
		var moved = $"Moved {moves.Count} file(s) into {target} from {string.Join(", ", candidates)}.";

		return duplicates == 0 ? [moved] : [moved, $"Dropped {duplicates} file(s) already there with the same contents."];
	}

	/// <summary>Whether a file is a candidate's own generated index, at its top level.</summary>
	private static bool IsGeneratedIndex(string relative, string file) =>
		relative.Equals(NoteIndexFile.FileName, StringComparison.OrdinalIgnoreCase)
			&& NoteFile.Read(file) is { } content
			&& NoteIndexFile.IsGenerated(content);

	private static bool Same(string left, string right) =>
		File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));

	/// <summary>
	/// Removes the folders a merge emptied, deepest first. Only empty ones: anything a merge did not
	/// move is still somebody's, and stays where it is.
	/// </summary>
	private static void RemoveEmpty(string directory)
	{
		foreach (var child in Directory.EnumerateDirectories(directory)) RemoveEmpty(child);

		if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
	}

	/// <summary>Several locks, released together.</summary>
	private sealed class Held : IDisposable
	{
		private readonly List<StoreLock> _locks = [];

		public void Add(StoreLock held) => _locks.Add(held);

		public void Dispose()
		{
			foreach (var held in _locks) held.Dispose();
		}
	}
}
