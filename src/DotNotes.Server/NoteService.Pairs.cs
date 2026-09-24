using System.Globalization;

using DotNotes.Contracts;
using DotNotes.Notes.Files;
using DotNotes.Notes.Stores;

namespace DotNotes.Server;

/// <summary>
/// A note kept in both stores: one note, two copies, joined by a <c>dn-id</c>.
/// <para>
/// A fact true of every branch reaches the other worktrees only when the branch that learned it
/// merges, and a worktree cut from an older commit never sees it. Keeping a private copy as well is
/// the answer; keeping it as one note is what stops that answer costing two hits and two edits.
/// </para>
/// <para>
/// Everything here travels toward the machine and never away. Writing the committed copy follows
/// into the private one, because making a fact private cannot publish anything. Writing the private
/// copy never reaches the committed one, because that would be a publication nobody asked for. And
/// retiring the committed copy retires the private ones, which git cannot reach. See
/// <c>docs/decisions/a-note-kept-in-both-stores-is-one-note.md</c>.
/// </para>
/// </summary>
public sealed partial class NoteService
{
	/// <summary>The authored key a retired note carries.</summary>
	private const string SupersededKey = "superseded";

	/// <summary>
	/// One note into both stores, under both locks, joined by an id kept from whichever copy already
	/// has one.
	/// </summary>
	/// <exception cref="McpRefusal">The two copies are different notes, or the one read has changed since.</exception>
	private NoteWritten WriteBoth(NoteStores stores, NoteDraft draft, string? revision)
	{
		var repoPath = Path.Combine(stores.Repo.Ensure(), $"{draft.Name}.md");
		var machineFolder = stores.Machine.Ensure();
		var notices = new List<string>();
		string composed;
		bool created;
		bool changed;

		using (Locks([stores.Repo.Path, stores.Machine.Path]))
		{
			var committed = NoteFile.Read(repoPath);
			var committedId = IdOf(committed);
			var machinePath = (committedId is null ? null : TwinPath(stores, NoteScope.Machine, committedId))
				?? Path.Combine(machineFolder, $"{draft.Name}.md");
			var kept = NoteFile.Read(machinePath);
			var keptId = IdOf(kept);

			// Two notes that happen to share a name are not one note, and writing both would replace
			// one of them with the other.
			var unrelated = committed is not null && kept is not null && (committedId is null || committedId != keptId);

			if (unrelated)
			{
				throw new McpRefusal(
					$"'{draft.Name}' is two different notes, one in each store. Rename one with note_move "
						+ "before keeping it in both.");
			}

			Guard(committed ?? kept, revision, draft.Name);

			var id = committedId ?? keptId ?? Guid.NewGuid().ToString("n")[..16];

			composed = NoteWriter.Compose(draft with { Scope = NoteScope.Repository, Id = id }, committed);
			created = committed is null;
			changed = NoteFile.Write(repoPath, composed);

			if (committed is not null && kept is not null && !SameNote(kept, committed))
			{
				notices.Add(Diverged(draft.Name, machinePath));
			}
			else
			{
				var privately = draft with { Scope = NoteScope.Machine, Id = id, Name = Path.GetFileNameWithoutExtension(machinePath) };

				changed |= NoteFile.Write(machinePath, NoteWriter.Compose(privately, kept));
			}

			Regenerate(stores, NoteScope.Repository);
			Regenerate(stores, NoteScope.Machine);
		}

		Record(stores);

		var heading = NoteReader.Parse(repoPath, NoteScope.Repository, composed).Heading;

		return Attribute(
			new NoteWritten
			{
				Note = heading with { Twin = NoteScope.Machine },
				Created = created,
				Changed = changed,
				Notices = [.. Notices(stores, StoreSelection.Both), .. notices],
			},
			stores,
			StoreSelection.Both);
	}

	/// <summary>
	/// Carries a write of a committed note into its private copy, where it has one on this machine and
	/// that copy still says what the committed one said before the write. A copy the person has edited
	/// since is theirs, and is reported rather than overwritten.
	/// </summary>
	/// <returns>A notice where the private copy was left alone, or null.</returns>
	private string? Mirror(NoteStores stores, NoteDraft draft, string previous)
	{
		if (IdOf(previous) is not { } id || !stores.Machine.IsAvailable) return null;
		if (TwinPath(stores, NoteScope.Machine, id) is not { } twin) return null;

		using var held = StoreLock.Take(stores.Machine.Path, options.StoreLockTimeout, options.LocalAppData);

		if (NoteFile.Read(twin) is not { } current) return null;

		if (!SameNote(current, previous)) return Diverged(draft.Name, twin);

		var privately = draft with { Scope = NoteScope.Machine, Id = id, Name = Path.GetFileNameWithoutExtension(twin) };

		NoteFile.Write(twin, NoteWriter.Compose(privately, current));
		Regenerate(stores, NoteScope.Machine);

		return null;
	}

	/// <summary>
	/// Retires a committed note, and its private copy with it.
	/// <para>
	/// A committed note that has ever been kept in both is superseded rather than deleted: some
	/// machine may hold a private copy, and only the file, travelling by git, can reach it. Deleting it
	/// would leave that copy answering forever. A committed note with no id is deleted outright.
	/// </para>
	/// </summary>
	private (bool Existed, bool Superseded) Retire(NoteStores stores, string slug, bool privateToo)
	{
		var path = Path.Combine(stores.Repo.Path, $"{slug}.md");
		var existed = false;
		var superseded = false;
		string? id = null;

		using (StoreLock.Take(stores.Repo.Path, options.StoreLockTimeout, options.LocalAppData))
		{
			if (NoteFile.Read(path) is { } content)
			{
				existed = true;
				id = IdOf(content);

				if (id is not null)
				{
					NoteFile.Write(path, Superseding(content, $"Retired on {Today()}"));
					superseded = true;
				}
				else
				{
					NoteFile.Delete(path);
				}

				Regenerate(stores, NoteScope.Repository);
			}
		}

		if (!stores.Machine.IsAvailable) return (existed, superseded);

		var twin = id is not null
			? TwinPath(stores, NoteScope.Machine, id)
			: privateToo ? Path.Combine(stores.Machine.Path, $"{slug}.md") : null;

		if (twin is null) return (existed, superseded);

		using (StoreLock.Take(stores.Machine.Path, options.StoreLockTimeout, options.LocalAppData))
		{
			if (NoteFile.Delete(twin))
			{
				existed = true;
				Regenerate(stores, NoteScope.Machine);
			}
		}

		return (existed, superseded);
	}

	/// <summary>
	/// Marks the private copy of every committed note this checkout sees superseded, so the worktrees
	/// whose branch does not have the retirement stop finding it too. Runs where the retirement is
	/// visible, because that is the only place it can be learned.
	/// </summary>
	private void Propagate(NoteStores stores)
	{
		if (!stores.Repo.IsAvailable || !stores.Machine.IsAvailable) return;

		var retired = search.Headings(stores, StoreSelection.Repository)
			.Where(heading => heading is { Superseded: not null, Id: not null })
			.ToArray();

		if (retired.Length == 0) return;

		var live = search.Headings(stores, StoreSelection.Machine)
			.Where(heading => heading is { Id: not null, Superseded: null } && stores.Machine.Holds(heading.Path))
			.GroupBy(heading => heading.Id!, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

		var due = retired.Where(heading => live.ContainsKey(heading.Id!)).ToArray();

		if (due.Length == 0) return;

		using var held = StoreLock.Take(stores.Machine.Path, options.StoreLockTimeout, options.LocalAppData);

		foreach (var heading in due)
		{
			var twin = live[heading.Id!].Path;

			if (NoteFile.Read(twin) is { } content) NoteFile.Write(twin, Superseding(content, heading.Superseded!));
		}

		Regenerate(stores, NoteScope.Machine);
	}

	/// <summary>The problems a note kept in both can have that a note kept in one cannot.</summary>
	private static IEnumerable<NoteProblem> PairFaults(IReadOnlyList<NoteHeading> headings)
	{
		var machine = headings
			.Where(heading => heading is { Scope: NoteScope.Machine, Id: not null })
			.GroupBy(heading => heading.Id!, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

		foreach (var heading in headings.Where(heading => heading.Scope == NoteScope.Repository))
		{
			if (heading.Superseded is { } why)
			{
				yield return Problem(heading, "superseded",
					$"Retired ({why}), so no search finds it. Delete the file once no machine still needs "
						+ "its private copy retired.");

				continue;
			}

			if (heading.Id is null || !machine.TryGetValue(heading.Id, out var twin)) continue;

			var committed = NoteFile.Read(heading.Path);
			var kept = NoteFile.Read(twin.Path);

			if (committed is null || kept is null || SameNote(kept, committed)) continue;

			yield return Problem(heading, "twin-differs",
				$"Its private copy at {twin.Path} says something else. Make them agree by hand, or delete the "
					+ "private copy with note_delete scope machine and write it again with scope both.");
		}
	}

	/// <summary>The copy of a note in one store, found by the id joining it to its other copy.</summary>
	private string? TwinPath(NoteStores stores, NoteScope scope, string id) =>
		search.Headings(stores, Selection(scope))
			.FirstOrDefault(heading => heading.Id == id && stores[scope].Holds(heading.Path))
			?.Path;

	/// <summary>A note's pairing id, or null for a note kept in one store or for no note.</summary>
	private static string? IdOf(string? content) =>
		content is null
			? null
			: NoteFrontmatter.Parse(FrontmatterBlock.Split(content).Yaml).Scalar(NoteFrontmatter.IdKey) is { Length: > 0 } id
				? id
				: null;

	/// <summary>
	/// Whether two copies say the same thing: their prose and their description. Not their scope,
	/// dates or topics, which differ between copies of one note by design.
	/// </summary>
	private static bool SameNote(string left, string right)
	{
		var a = NoteReader.Parse(string.Empty, NoteScope.Machine, left);
		var b = NoteReader.Parse(string.Empty, NoteScope.Machine, right);

		return a.Heading.Description == b.Heading.Description
			&& a.Body.ReplaceLineEndings("\n").Trim() == b.Body.ReplaceLineEndings("\n").Trim();
	}

	private static string Superseding(string content, string why) =>
		FrontmatterSplice.Apply(content, new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			[SupersededKey] = FrontmatterSplice.Entry(SupersededKey, why),
		});

	private static string Diverged(string name, string path) =>
		$"The private copy of '{name}' at {path} has been edited since, so it was left as it is. "
			+ "note_check reports the difference.";

	private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
