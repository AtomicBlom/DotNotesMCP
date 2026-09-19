using DotNotes.Contracts;
using DotNotes.Notes;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Stores;

namespace DotNotes.Index;

/// <summary>
/// Searching by reading the store and building an index in memory.
/// <para>
/// The index is cached per store and thrown away when anything in it changes, judged by the newest
/// write time and the file count. That is cheap enough to check on every call and exact enough for
/// a store one person edits: a change made in Obsidian is visible to the next search, which is the
/// freshness rule this server's whole premise rests on.
/// </para>
/// </summary>
public sealed class CrawlingNoteSearch(NoteOptions options) : INoteSearch
{
	private readonly Dictionary<string, (Stamp Stamp, SearchIndex Index)> _cached = new(StringComparer.Ordinal);
	private readonly Lock _gate = new();

	/// <inheritdoc />
	public string Backend => "crawl";

	/// <inheritdoc />
	public IReadOnlyList<NoteHit> Search(NoteStores stores, NoteQuery query)
	{
		var weighting = new SearchWeighting { Repository = stores.Repository.Key };
		var terms = Tokenizer.Query(query.Text);
		var hits = new List<NoteHit>();

		foreach (var scope in Scopes(query.Scope))
		{
			var index = IndexFor(stores, scope);

			foreach (var (note, score) in index.Search(query.Text, weighting))
			{
				if (!Matches(note.Heading, query)) continue;

				hits.Add(new NoteHit
				{
					Heading = note.Heading,
					Extract = Snippet.Of(note.Body, terms),
					Score = score,
					OtherMachine = IsElsewhere(note.Heading, stores.MachineName),
				});
			}
		}

		return [.. hits.OrderByDescending(hit => hit.Score)
			.ThenBy(hit => hit.Heading.Name, StringComparer.Ordinal)
			.Take(Math.Clamp(query.Limit, 1, 50))];
	}

	/// <inheritdoc />
	public NoteHit? Find(NoteStores stores, string name, StoreSelection scope)
	{
		var slug = Slug.Of(name);

		foreach (var store in Scopes(scope))
		{
			foreach (var note in IndexFor(stores, store).Notes)
			{
				if (!note.Heading.Name.Equals(slug, StringComparison.Ordinal)) continue;

				return new NoteHit
				{
					Heading = note.Heading,
					Extract = note.Body,
					Score = 0,
					OtherMachine = IsElsewhere(note.Heading, stores.MachineName),
				};
			}
		}

		return null;
	}

	/// <inheritdoc />
	public IReadOnlyList<NoteHeading> Headings(NoteStores stores, StoreSelection scope) =>
		[.. Scopes(scope)
			.SelectMany(store => IndexFor(stores, store).Notes)
			.Select(note => note.Heading)
			.OrderBy(heading => heading.Name, StringComparer.Ordinal)];

	/// <summary>The stores a selection covers, skipping any that cannot be read.</summary>
	private static IEnumerable<NoteScope> Scopes(StoreSelection selection) => selection switch
	{
		StoreSelection.Machine => [NoteScope.Machine],
		StoreSelection.Repository => [NoteScope.Repository],
		_ => [NoteScope.Machine, NoteScope.Repository],
	};

	/// <summary>Whether a note says it is about machines and this is not one of them.</summary>
	private static bool IsElsewhere(NoteHeading heading, string machine) =>
		heading.Machines.Count > 0
			&& !heading.Machines.Contains(machine, StringComparer.OrdinalIgnoreCase);

	/// <summary>The facets, which narrow what the score already ordered.</summary>
	private static bool Matches(NoteHeading heading, NoteQuery query)
	{
		if (query.Type is { } type && heading.Type != type) return false;

		return query.Tags.Count == 0
			|| query.Tags.All(tag => heading.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>The index for one store, rebuilt when the store has changed under it.</summary>
	private SearchIndex IndexFor(NoteStores stores, NoteScope scope)
	{
		var store = stores[scope];

		if (!store.IsAvailable || !Directory.Exists(store.Path)) return SearchIndex.Build([]);

		var stamp = Stamp.Of(store.Path);

		lock (_gate)
		{
			if (_cached.TryGetValue(store.Path, out var cached) && cached.Stamp == stamp)
			{
				return cached.Index;
			}

			var index = SearchIndex.Build(Crawl(store.Path, scope));

			_cached[store.Path] = (stamp, index);

			return index;
		}
	}

	/// <summary>
	/// Every note in a store. The generated index is skipped: a listing of every note would match
	/// every query, and it says nothing a search of the notes themselves does not.
	/// </summary>
	private IEnumerable<IndexedNote> Crawl(string path, NoteScope scope)
	{
		foreach (var file in Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories))
		{
			var content = NoteFile.Read(file);

			if (content is null || NoteIndexFile.IsGenerated(content)) continue;
			if (content.Length > options.MaxBodyCharacters * 8) continue;

			yield return IndexedNote.Of(NoteReader.Parse(file, scope, content));
		}
	}
}

/// <summary>
/// What a store looked like, cheaply enough to ask on every call.
/// <para>
/// A count and the newest write time. Not a hash of every file, which would cost the read the cache
/// exists to avoid -- and not a file watcher, which misses a change made while the process was not
/// running and has to be reconciled at startup anyway.
/// </para>
/// </summary>
internal readonly record struct Stamp(int Files, long Newest)
{
	public static Stamp Of(string path)
	{
		var files = 0;
		var newest = 0L;

		foreach (var file in Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories))
		{
			files++;
			newest = Math.Max(newest, File.GetLastWriteTimeUtc(file).Ticks);
		}

		return new Stamp(files, newest);
	}
}
