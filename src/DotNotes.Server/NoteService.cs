using DotNotes.Contracts;
using DotNotes.Notes;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Stores;

namespace DotNotes.Server;

/// <summary>
/// Everything the tools do. The tools themselves hold no logic: one call in, one return.
/// <para>
/// This is also the one place a result is told which repository answered. There is no process
/// boundary to enforce that, so what actually holds it is the test that asserts it over every
/// registered tool -- the method is the convention, and the test is the guarantee.
/// </para>
/// </summary>
public sealed class NoteService(NoteOptions options, INoteSearch search)
{
	/// <summary>Ranked hits for a query.</summary>
	public NoteSearchResult Search(string? query, string? scope, string? type, string[]? tags, int limit)
	{
		var selection = ArgumentValues.ReadScope(scope);
		var stores = Stores();
		var request = new NoteQuery
		{
			Text = query,
			Scope = selection,
			Type = ArgumentValues.Type(type),
			Tags = tags ?? [],
			Limit = limit,
		};

		var hits = search.Search(stores, request);
		var searched = search.Headings(stores, selection).Count;
		var notices = Notices(stores, selection).ToList();

		// An empty store and a store with nothing matching look identical in a result, and a caller
		// who reads the first as the second concludes there is nothing to find and stops asking.
		// Saying which, and what to do about it, is what keeps the next call from going to the files.
		if (searched == 0 && stores.Machine.IsAvailable)
		{
			notices.Add(
				$"No notes yet for {stores.Repository.Name}. note_write records the first.");
		}

		return Attribute(
			new NoteSearchResult
			{
				Matches = [.. hits.Select(Match)],
				Searched = searched,
				Truncated = hits.Count >= Math.Clamp(limit, 1, 50) && searched > hits.Count,
				Backend = search.Backend,
				Notices = notices,
			},
			stores,
			selection);
	}

	/// <summary>One note whole, with what points at it.</summary>
	/// <exception cref="McpRefusal">Nothing of that name is in the stores searched.</exception>
	public NoteContent Read(string name, string? scope)
	{
		var selection = ArgumentValues.ReadScope(scope);
		var stores = Stores();
		var hit = search.Find(stores, name, selection)
			?? throw new McpRefusal(
				$"No note called '{name}' in {Describe(selection)} for {stores.Repository.Name}. "
					+ "note_search with no query lists what is there.");

		var headings = search.Headings(stores, selection);

		return Attribute(
			new NoteContent
			{
				Note = hit.Heading,
				Body = hit.Extract,
				Links = [.. Links(hit, headings)],
				Backlinks = [.. Backlinks(stores, hit.Heading.Name, selection)],
				Notices = Notices(stores, selection),
			},
			stores,
			selection);
	}

	/// <summary>
	/// Writes a note, creating it or replacing one that is there.
	/// <para>
	/// Under the store's lock, because the same store is written by every session on this machine and
	/// the index beside the note is regenerated in the same breath. The lock is an open handle
	/// outside the store, so a session that dies holds nothing.
	/// </para>
	/// </summary>
	/// <exception cref="McpRefusal">The store is unavailable, the note is too long, or it changed underneath.</exception>
	public NoteWritten Write(
		string name,
		string description,
		string body,
		string scope,
		string? type,
		string[]? tags,
		string[]? machines,
		string? revision)
	{
		var target = ArgumentValues.WriteScope(scope);
		var stores = Stores();
		var store = stores[target];

		if (store.Unavailable is { } because) throw new McpRefusal(because);

		if (NoteWriter.TooLong(body, options.MaxBodyCharacters) is { } tooLong)
		{
			throw new McpRefusal(tooLong);
		}

		var slug = Slug.Of(name);
		var path = Path.Combine(store.Ensure(), $"{slug}.md");

		using var held = StoreLock.Take(store.Path, options.StoreLockTimeout, options.LocalAppData);

		var existing = NoteFile.Read(path);

		Guard(existing, revision, slug);

		var draft = new NoteDraft
		{
			Name = slug,
			Description = description,
			Body = body,
			Scope = target,
			Repository = stores.Repository.Key,
			Type = ArgumentValues.Type(type) ?? NoteType.Project,
			Tags = tags ?? [],
			Machines = machines ?? [],
		};

		var composed = NoteWriter.Compose(draft, existing);
		var changed = NoteFile.Write(path, composed);

		Regenerate(stores, target);

		return Attribute(
			new NoteWritten
			{
				Note = NoteReader.Parse(path, target, composed).Heading,
				Created = existing is null,
				Changed = changed,
				Notices = Notices(stores, Selection(target)),
			},
			stores,
			Selection(target));
	}

	/// <summary>Removes a note, and says what now links to nothing.</summary>
	/// <exception cref="McpRefusal">The store is unavailable.</exception>
	public NoteDeleted Delete(string name, string scope)
	{
		var target = ArgumentValues.WriteScope(scope);
		var stores = Stores();
		var store = stores[target];

		if (store.Unavailable is { } because) throw new McpRefusal(because);

		var slug = Slug.Of(name);
		var path = Path.Combine(store.Path, $"{slug}.md");

		using var held = StoreLock.Take(store.Path, options.StoreLockTimeout, options.LocalAppData);

		var dangling = Backlinks(stores, slug, StoreSelection.Both).ToArray();
		var existed = NoteFile.Delete(path);

		if (existed) Regenerate(stores, target);

		return Attribute(
			new NoteDeleted
			{
				Name = slug,
				Existed = existed,
				LeftDangling = dangling,
				Notices = Notices(stores, Selection(target)),
			},
			stores,
			Selection(target));
	}

	/// <summary>Where this is, and where the notes are.</summary>
	public NoteContextResult Context(string? directory)
	{
		var stores = Stores(directory);
		var identity = stores.Repository;

		return Attribute(
			new NoteContextResult
			{
				Directory = identity.Origin,
				Kind = identity.Kind.ToString(),
				Worktree = identity.Worktree,
				Root = identity.Root,
				Remote = identity.Remote,
				NamedBy = identity.NamedBy.ToString(),
				Machine = stores.MachineName,
				MachineStore = State(stores, NoteScope.Machine, StoreSelection.Machine),
				RepositoryStore = State(stores, NoteScope.Repository, StoreSelection.Repository),
			},
			stores,
			StoreSelection.Both);
	}

	/// <summary>
	/// The stores for this call: the directory a client named, else the one the process was started
	/// in. Resolved per call rather than once, because one session moves between repositories.
	/// </summary>
	private NoteStores Stores(string? directory = null) =>
		NoteStores.For(directory ?? CallOrigin.Directory ?? options.DefaultRoot, options);

	/// <summary>
	/// Fills in which repository answered, at the one point every result passes through.
	/// </summary>
	private static T Attribute<T>(T result, NoteStores stores, StoreSelection scope)
		where T : NoteResult =>
		result with { Repository = stores.Repository.Key, Scope = Describe(scope) };

	/// <summary>The selection that names exactly one store.</summary>
	private static StoreSelection Selection(NoteScope scope) =>
		scope == NoteScope.Repository ? StoreSelection.Repository : StoreSelection.Machine;

	/// <summary>
	/// Refuses a write over a note that has changed since the caller read it.
	/// <para>
	/// These are files a person edits in Obsidian while a session is running, so an unconditional
	/// write is a way to lose an edit somebody made thirty seconds ago and never find out. Creating a
	/// note needs no revision -- there is nothing to lose -- and replacing one does.
	/// </para>
	/// </summary>
	private static void Guard(string? existing, string? revision, string name)
	{
		if (existing is null) return;

		var current = NoteFile.Revision(existing);

		if (revision is null or "")
		{
			throw new McpRefusal(
				$"'{name}' already exists. Read it first and pass its revision ({current}) to replace "
					+ "it, or write under a different name.");
		}

		if (!revision.Equals(current, StringComparison.OrdinalIgnoreCase))
		{
			throw new McpRefusal(
				$"'{name}' has changed since revision {revision} -- it is now {current}. Read it again "
					+ "before replacing it; the user edits these files directly.");
		}
	}

	/// <summary>
	/// Rewrites the index beside a store's notes. Under the same lock as the write that prompted it,
	/// so two sessions cannot interleave a note and an index that disagree.
	/// </summary>
	private void Regenerate(NoteStores stores, NoteScope scope)
	{
		var store = stores[scope];
		if (!store.IsAvailable) return;

		var selection = Selection(scope);
		var headings = search.Headings(stores, selection);
		var other = scope == NoteScope.Machine && stores.Repo.IsAvailable
			? search.Headings(stores, StoreSelection.Repository)
			: null;

		var path = Path.Combine(store.Path, NoteIndexFile.FileName);
		var lineEnding = NoteFile.Read(path) is { } current
			? FrontmatterBlock.Split(current).LineEnding
			: "\n";

		NoteFile.Write(path, NoteIndexFile.Render(stores.Repository.Name, headings, lineEnding, other));
	}

	private static string Describe(StoreSelection scope) => scope switch
	{
		StoreSelection.Machine => "machine",
		StoreSelection.Repository => "repository",
		_ => "both",
	};

	private static NoteMatch Match(NoteHit hit) => new()
	{
		Note = hit.Heading,
		Extract = hit.Extract,
		Score = Math.Round(hit.Score, 3),
		OtherMachine = hit.OtherMachine,
	};

	private NoteStoreState State(NoteStores stores, NoteScope scope, StoreSelection selection)
	{
		var store = stores[scope];

		return new NoteStoreState
		{
			Path = store.Path,
			Writable = store.IsAvailable,
			Notes = store.IsAvailable ? search.Headings(stores, selection).Count : 0,
			Unavailable = store.Unavailable,
		};
	}

	/// <summary>
	/// A store that refused says so on every answer it could not contribute to. Silence here is the
	/// failure this server is most careful about: an empty result reads as "nothing to find", and a
	/// caller who believes that stops asking.
	/// </summary>
	private static IReadOnlyList<string> Notices(NoteStores stores, StoreSelection selection)
	{
		var notices = new List<string>();

		if (selection != StoreSelection.Repository && stores.Machine.Unavailable is { } machine)
		{
			notices.Add(machine);
		}

		if (selection != StoreSelection.Machine && stores.Repo.Unavailable is { } repository)
		{
			notices.Add(repository);
		}

		return notices;
	}

	/// <summary>A note's links, each said to resolve or not.</summary>
	private static IEnumerable<NoteLink> Links(NoteHit hit, IReadOnlyList<NoteHeading> headings)
	{
		var known = headings.Select(heading => heading.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var link in Wikilink.In(hit.Extract))
		{
			var target = link.Target[(link.Target.LastIndexOf('/') + 1)..];

			yield return new NoteLink
			{
				Target = target,
				Scope = link.Scope?.ToString().ToLowerInvariant(),
				Resolved = known.Contains(target),
			};
		}
	}

	/// <summary>
	/// What points at a note. The half Obsidian shows and reading the file does not, and often the
	/// more useful half: the notes that reference this one are the context it was written in.
	/// </summary>
	private IEnumerable<NoteHeading> Backlinks(NoteStores stores, string name, StoreSelection selection)
	{
		foreach (var heading in search.Headings(stores, selection))
		{
			if (heading.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

			var body = NoteFile.Read(heading.Path);
			if (body is null) continue;

			var links = Wikilink.In(FrontmatterBlock.Split(body).Body);

			if (links.Any(link =>
				link.Target[(link.Target.LastIndexOf('/') + 1)..]
					.Equals(name, StringComparison.OrdinalIgnoreCase)))
			{
				yield return heading;
			}
		}
	}
}

/// <summary>
/// Something the caller asked for that cannot be given, with the fix in the message. Its own type so
/// the boundary can tell a refusal apart from a fault, which is what decides whether a retry is worth
/// anything.
/// </summary>
public sealed class McpRefusal(string message) : Exception(message);
