using DotNotes.Contracts;
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

		return Attribute(
			new NoteSearchResult
			{
				Matches = [.. hits.Select(Match)],
				Searched = searched,
				Truncated = hits.Count >= Math.Clamp(limit, 1, 50) && searched > hits.Count,
				Backend = search.Backend,
				Notices = Notices(stores, selection),
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
