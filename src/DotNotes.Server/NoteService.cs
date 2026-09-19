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
public sealed partial class NoteService(NoteOptions options, INoteSearch search)
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

	/// <summary>
	/// Renames a note, moves it between stores, or both, and points every link at where it went.
	/// </summary>
	/// <exception cref="McpRefusal">The note is absent, the destination is unavailable, or nothing was asked for.</exception>
	public NoteMoved Move(string name, string? toName, string? toScope)
	{
		var stores = Stores();
		var from = Locate(stores, Slug.Of(name));
		var target = toScope is { Length: > 0 } ? ArgumentValues.WriteScope(toScope) : from.Heading.Scope;
		var renamed = toName is { Length: > 0 } ? Slug.Of(toName) : from.Heading.Name;

		if (renamed == from.Heading.Name && target == from.Heading.Scope)
		{
			throw new McpRefusal(
				$"'{from.Heading.Name}' is already called that, in the {Describe(Selection(target))} store. "
					+ "Give toName, toScope, or both.");
		}

		var destination = stores[target];
		if (destination.Unavailable is { } because) throw new McpRefusal(because);

		var collision = Path.Combine(destination.Path, $"{renamed}.md");
		if (!string.Equals(collision, from.Heading.Path, StringComparison.OrdinalIgnoreCase)
			&& File.Exists(collision))
		{
			throw new McpRefusal($"'{renamed}' already exists in the {Describe(Selection(target))} store.");
		}

		using var held = StoreLock.Take(destination.Path, options.StoreLockTimeout, options.LocalAppData);

		var content = NoteFile.Read(from.Heading.Path)
			?? throw new McpRefusal($"'{from.Heading.Name}' could not be read from {from.Heading.Path}.");

		var moved = FrontmatterSplice.Apply(content, new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["name"] = FrontmatterSplice.Entry("name", renamed),
			["scope"] = FrontmatterSplice.Entry("scope", target.ToString().ToLowerInvariant()),
		});

		NoteFile.Write(collision, moved);

		if (!string.Equals(collision, from.Heading.Path, StringComparison.OrdinalIgnoreCase))
		{
			NoteFile.Delete(from.Heading.Path);
		}

		var rewritten = Follow(stores, from.Heading.Name, renamed, target);

		Regenerate(stores, target);
		if (target != from.Heading.Scope) Regenerate(stores, from.Heading.Scope);

		return Attribute(
			new NoteMoved
			{
				Note = NoteReader.Parse(collision, target, moved).Heading,
				FromName = from.Heading.Name,
				FromScope = Describe(Selection(from.Heading.Scope)),
				LinksRewritten = rewritten,
				Notices = Notices(stores, StoreSelection.Both),
			},
			stores,
			Selection(target));
	}

	/// <summary>Everything wrong in the stores, which is nothing a search would show.</summary>
	public NoteCheckReport Check(string? scope)
	{
		var selection = ArgumentValues.ReadScope(scope);
		var stores = Stores();
		var headings = search.Headings(stores, selection);
		var known = headings.Select(heading => heading.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var problems = new List<NoteProblem>();
		var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var heading in headings)
		{
			var content = NoteFile.Read(heading.Path);
			if (content is null) continue;

			var note = NoteReader.Parse(heading.Path, heading.Scope, content);

			problems.AddRange(Faults(note, known, seen, stores));
		}

		problems.AddRange(Conflicts(stores, selection));

		return Attribute(
			new NoteCheckReport
			{
				Problems = [.. problems.OrderBy(problem => problem.Note, StringComparer.Ordinal)
					.ThenBy(problem => problem.Kind, StringComparer.Ordinal)],
				Checked = headings.Count,
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

	/// <summary>A note by name, or a refusal saying how to find out what there is.</summary>
	/// <exception cref="McpRefusal">Nothing of that name is in either store.</exception>
	private NoteHit Locate(NoteStores stores, string name) =>
		search.Find(stores, name, StoreSelection.Both)
			?? throw new McpRefusal(
				$"No note called '{name}' for {stores.Repository.Name}. "
					+ "note_search with no query lists what is there.");

	/// <summary>
	/// Points every link at a note's new name and store, across both stores.
	/// <para>
	/// Both, always, whichever store the note moved within: a committed note may be linked from a
	/// private one and the other way round, and rewriting only the store that changed leaves the
	/// other half pointing at a name nothing answers to.
	/// </para>
	/// </summary>
	private int Follow(NoteStores stores, string oldName, string newName, NoteScope target)
	{
		var rewritten = 0;

		foreach (var heading in search.Headings(stores, StoreSelection.Both))
		{
			if (heading.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)) continue;

			var content = NoteFile.Read(heading.Path);
			if (content is null) continue;

			var block = FrontmatterBlock.Split(content);
			var (body, count) = LinkRewrite.Retarget(block.Body, oldName, newName, target, heading.Scope);

			if (count == 0) continue;

			NoteFile.Write(heading.Path, content[..^block.Body.Length] + body);
			rewritten += count;
		}

		return rewritten;
	}

	/// <summary>
	/// What is wrong with one note.
	/// <para>
	/// Orphans are deliberately not here. A note nothing links to is the ordinary state of most
	/// notes, so reporting them would mark almost everything as a problem -- and a report that is
	/// mostly noise is a report people stop reading, which costs more than the few real orphans
	/// it would have surfaced.
	/// </para>
	/// </summary>
	private IEnumerable<NoteProblem> Faults(
		Note note,
		HashSet<string> known,
		Dictionary<string, string> seen,
		NoteStores stores)
	{
		var heading = note.Heading;

		foreach (var link in note.Links)
		{
			var target = LinkRewrite.Bare(link.Target);

			if (known.Contains(target)) continue;

			yield return Problem(heading, "dangling-link",
				$"[[{link.Target}]] names no note. Rename the target with note_move, or fix the link.");
		}

		if (NoteWriter.TooLong(note.Body, options.MaxBodyCharacters) is { } tooLong)
		{
			yield return Problem(heading, "oversized", tooLong);
		}

		if (note.Frontmatter.Error is { } error)
		{
			yield return Problem(heading, "unreadable-frontmatter",
				$"Its properties do not parse ({error}), so its tags and type are not being read.");
		}

		// A note whose declared scope is not the store it sits in was moved by hand, and the two
		// disagree about what it is. The file wins, because it is where the note actually is.
		var declared = note.Frontmatter.Scalar("scope");
		if (declared is { Length: > 0 } && !declared.Equals(heading.Scope.ToString(), StringComparison.OrdinalIgnoreCase))
		{
			yield return Problem(heading, "misfiled",
				$"It says scope: {declared} but sits in the {Describe(Selection(heading.Scope))} store. "
					+ "note_move puts it where it says, or rewrite the property.");
		}

		if (seen.TryGetValue(heading.Name, out var first))
		{
			yield return Problem(heading, "duplicate-name",
				$"'{heading.Name}' is also claimed by {first}. A link to it reaches one of them, "
					+ "unpredictably; rename one with note_move.");
		}
		else
		{
			seen[heading.Name] = heading.Path;
		}

		_ = stores;
	}

	/// <summary>
	/// Files a sync service left behind. Drive names a conflicted copy by appending a number, which
	/// arrives in the store as an extra note with the same <c>name</c> property as the original --
	/// so it is found before it becomes a duplicate nobody can explain.
	/// </summary>
	private IEnumerable<NoteProblem> Conflicts(NoteStores stores, StoreSelection selection)
	{
		foreach (var scope in selection == StoreSelection.Repository
			? new[] { NoteScope.Repository }
			: selection == StoreSelection.Machine ? [NoteScope.Machine] : [NoteScope.Machine, NoteScope.Repository])
		{
			var store = stores[scope];
			if (!store.IsAvailable || !Directory.Exists(store.Path)) continue;

			foreach (var file in Directory.EnumerateFiles(store.Path, "*.md", SearchOption.AllDirectories))
			{
				var name = Path.GetFileNameWithoutExtension(file);

				if (!ConflictCopy().IsMatch(name)) continue;

				yield return new NoteProblem
				{
					Kind = "sync-conflict",
					Note = name,
					Detail = "A sync service left a conflicted copy here. Compare it with the original "
						+ "and delete whichever is spent.",
					Path = file,
				};
			}
		}
	}

	private static NoteProblem Problem(NoteHeading heading, string kind, string detail) => new()
	{
		Kind = kind,
		Note = heading.Name,
		Detail = detail,
		Path = heading.Path,
	};

	/// <summary>A name ending in a parenthesised number, which is how every sync service names a copy.</summary>
	[System.Text.RegularExpressions.GeneratedRegex(@" \(\d+\)$")]
	private static partial System.Text.RegularExpressions.Regex ConflictCopy();

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
