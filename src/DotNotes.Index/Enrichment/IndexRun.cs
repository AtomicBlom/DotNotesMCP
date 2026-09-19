using DotNotes.Contracts;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Files;
using DotNotes.Notes.Stores;

namespace DotNotes.Index.Enrichment;

/// <summary>
/// The indexing loop: hand out a note, take an enrichment back, and know what is left.
/// <para>
/// There is no watermark. Notes have no natural order, and a hand-edit in the middle of a run
/// breaks any ordering a watermark would rely on -- silently, by skipping whatever moved behind it.
/// Freshness is instead a property of each note, computed by comparing what the note says about its
/// own enrichment to what it should say, so "what is left" is a scan and resuming is running it
/// again.
/// </para>
/// </summary>
public sealed class IndexRun
{
	/// <summary>
	/// How many times a note is tried before it is left alone. Three, because the failures worth
	/// retrying are transient and the rest repeat forever, and a loop that cannot finish is worse
	/// than a corpus with three unindexed notes in it.
	/// </summary>
	private const int RetryLimit = 3;

	private readonly NoteOptions _options;
	private readonly INoteSearch _search;
	private readonly string _promptHash;
	private readonly string _run = Guid.NewGuid().ToString("n")[..8];

	public IndexRun(NoteOptions options, INoteSearch search, string prompt)
	{
		_options = options;
		_search = search;
		_promptHash = IndexStamp.HashOf(prompt);
	}

	/// <summary>What the instructions hash to, which every note is stamped with.</summary>
	public string PromptHash => _promptHash;

	/// <summary>
	/// Claims the next note needing enrichment.
	/// <para>
	/// Never-indexed notes go first, because a note with no enrichment is invisible to a search where
	/// a stale one is only slightly wrong. Within that, most-linked first, so a run killed part way
	/// through has done the notes everything else points at.
	/// </para>
	/// </summary>
	public NoteAssignment Next(NoteStores stores, NoteScope scope, int leaseMinutes)
	{
		var store = stores[scope];
		var now = DateTimeOffset.UtcNow;

		using var held = StoreLock.Take(store.Path, _options.StoreLockTimeout, _options.LocalAppData);

		var journal = IndexJournal.Read(store.Path, _options.LocalAppData).WithoutExpired(now);
		var notes = Notes(stores, scope);
		var states = notes.Select(note => State(note, journal)).ToArray();
		var progress = Progress(states, journal, now);

		var queue = states
			.Where(state => state.Reason is not null)
			.Where(state => !journal.IsHeld(state.Note.Heading.Name, now))
			.OrderBy(state => Band(state.Reason!.Value))
			.ThenByDescending(state => state.Note.InboundLinks)
			.ThenBy(state => state.Note.Heading.Name, StringComparer.Ordinal)
			.ToArray();

		if (queue.Length == 0)
		{
			var blocked = states.Any(state => state.Reason is not null);

			return new NoteAssignment
			{
				State = blocked ? AssignmentState.Blocked : AssignmentState.Drained,
				Progress = progress,
				BlockedReason = blocked
					? "Every note that needs enriching is claimed by another run. Wait for a lease to "
						+ "expire, or check note_index_status."
					: null,
			};
		}

		var candidate = queue[0];
		var token = $"{_run}:{candidate.Note.Heading.Name}:{now.Ticks:x}";
		var lease = new IndexJournal.Lease
		{
			Note = candidate.Note.Heading.Name,
			Token = token,
			Run = _run,
			Taken = now,
			Expires = now.AddMinutes(Math.Clamp(leaseMinutes, 1, 120)),
		};

		(journal with { Leases = [.. journal.Leases, lease] }).Write(store.Path, _options.LocalAppData);

		return new NoteAssignment
		{
			State = AssignmentState.Assigned,
			Note = WorkItem(candidate, lease, notes, stores, scope),
			Progress = progress with { Claimed = progress.Claimed + 1, Remaining = progress.Remaining },
		};
	}

	/// <summary>
	/// Writes an enrichment into its note and releases the claim.
	/// <para>
	/// A lease that has expired is still accepted while nothing else has touched the note. Twelve
	/// minutes on a long note is correct work, and discarding it costs a turn and buys nothing; what
	/// is refused is a write over a note somebody else has since enriched or edited.
	/// </para>
	/// </summary>
	/// <exception cref="ArgumentException">The enrichment is misshapen, or the claim is not valid.</exception>
	public EnrichmentAccepted Write(
		NoteStores stores,
		NoteScope scope,
		string token,
		Contracts.Enrichment enrichment,
		IReadOnlyList<string> newTopics)
	{
		var store = stores[scope];
		var now = DateTimeOffset.UtcNow;

		using var held = StoreLock.Take(store.Path, _options.StoreLockTimeout, _options.LocalAppData);

		var journal = IndexJournal.Read(store.Path, _options.LocalAppData);
		var name = NameOf(token);
		var notes = Notes(stores, scope);
		var note = notes.FirstOrDefault(
				candidate => candidate.Heading.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			?? throw new ArgumentException($"'{name}' is no longer in this store.");

		var vocabulary = Vocabulary(notes);

		if (EnrichmentShape.Fault(enrichment, [.. vocabulary.Select(topic => topic.Name)], newTopics) is { } fault)
		{
			throw new ArgumentException(fault);
		}

		var content = NoteFile.Read(note.Heading.Path)
			?? throw new ArgumentException($"'{name}' could not be read.");

		Verify(journal, token, name, content, now);

		var known = notes.Select(other => other.Heading.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var kept = enrichment.Links.Where(link => known.Contains(LinkRewrite.Bare(link))).ToArray();
		var dropped = enrichment.Links.Except(kept, StringComparer.OrdinalIgnoreCase).ToArray();

		var written = NoteFile.Write(note.Heading.Path, Splice(content, enrichment, kept, store.Path));
		var released = journal with
		{
			Leases = [.. journal.Leases.Where(lease => lease.Token != token)],
			Attempts = [.. journal.Attempts.Where(attempt => !Names(attempt.Note, name))],
			Forced = [.. journal.Forced.Where(forced => !Names(forced, name))],
		};

		released.Write(store.Path, _options.LocalAppData);

		var after = Notes(stores, scope).Select(other => State(other, released)).ToArray();

		return new EnrichmentAccepted
		{
			Name = name,
			NoteRewritten = written,
			TopicsAdded = [.. newTopics.Where(topic => !vocabulary.Any(use => Names(use.Name, topic)))],
			LinksDropped = dropped,
			Progress = Progress(after, released, now),
		};
	}

	/// <summary>Gives a claim back, recording what it means.</summary>
	public SkipRecorded Skip(NoteStores stores, NoteScope scope, string token, SkipDisposition disposition, string? reason)
	{
		var store = stores[scope];
		var now = DateTimeOffset.UtcNow;

		using var held = StoreLock.Take(store.Path, _options.StoreLockTimeout, _options.LocalAppData);

		var journal = IndexJournal.Read(store.Path, _options.LocalAppData);
		var name = NameOf(token);
		var notes = Notes(stores, scope);
		var note = notes.FirstOrDefault(candidate => Names(candidate.Heading.Name, name));
		var hash = note is null ? string.Empty : NoteFile.SourceHash(NoteFile.Read(note.Heading.Path) ?? string.Empty);

		var without = journal with { Leases = [.. journal.Leases.Where(lease => lease.Token != token)] };

		var updated = disposition switch
		{
			SkipDisposition.NotWorthIndexing => without with
			{
				Skips = [.. without.Skips.Where(skip => !Names(skip.Note, name)),
					new IndexJournal.Skip { Note = name, Hash = hash, Reason = reason ?? "not worth indexing" }],
			},
			SkipDisposition.Unreadable => without with { Attempts = Failed(without, name, hash, reason) },
			_ => without,
		};

		updated.Write(store.Path, _options.LocalAppData);

		var after = notes.Select(other => State(other, updated)).ToArray();

		return new SkipRecorded
		{
			Name = name,
			Disposition = disposition.ToString(),
			Progress = Progress(after, updated, now),
		};
	}

	/// <summary>How the run is going, and what the vocabulary looks like.</summary>
	public IndexStatus Status(NoteStores stores, NoteScope scope, bool includeDrift)
	{
		var store = stores[scope];
		var now = DateTimeOffset.UtcNow;
		var journal = IndexJournal.Read(store.Path, _options.LocalAppData).WithoutExpired(now);
		var notes = Notes(stores, scope);
		var states = notes.Select(note => State(note, journal)).ToArray();

		return new IndexStatus
		{
			Progress = Progress(states, journal, now),
			SchemaVersion = IndexStamp.CurrentSchema,
			PromptHash = _promptHash,
			Topics = Vocabulary(notes),
			Leases = [.. journal.Leases.Select(lease => new LeaseHolder
			{
				Note = lease.Note,
				Run = lease.Run,
				Expires = lease.Expires,
			})],
			Drift = includeDrift ? Drift(notes) : null,
		};
	}

	/// <summary>Puts a selection of notes back in the queue.</summary>
	public IndexStatus Rebuild(NoteStores stores, NoteScope scope, RebuildSelection selection)
	{
		var store = stores[scope];

		using var held = StoreLock.Take(store.Path, _options.StoreLockTimeout, _options.LocalAppData);

		var journal = IndexJournal.Read(store.Path, _options.LocalAppData);
		var notes = Notes(stores, scope);

		var chosen = selection switch
		{
			RebuildSelection.Failed => journal.Attempts.Select(attempt => attempt.Note),
			RebuildSelection.Skipped => journal.Skips.Select(skip => skip.Note),
			RebuildSelection.Stale => notes.Where(note => State(note, journal).Reason is not null)
				.Select(note => note.Heading.Name),
			_ => notes.Select(note => note.Heading.Name),
		};

		var forced = chosen.ToArray();
		var cleared = journal with
		{
			Forced = [.. journal.Forced.Union(forced, StringComparer.OrdinalIgnoreCase)],
			Attempts = selection is RebuildSelection.Failed or RebuildSelection.All ? [] : journal.Attempts,
			Skips = selection is RebuildSelection.Skipped or RebuildSelection.All ? [] : journal.Skips,
		};

		cleared.Write(store.Path, _options.LocalAppData);

		return Status(stores, scope, includeDrift: false);
	}

	/// <summary>The notes in a store, as the index sees them.</summary>
	private IReadOnlyList<IndexedNote> Notes(NoteStores stores, NoteScope scope) =>
		[.. SearchIndex.Build(
			_search.Headings(stores, scope == NoteScope.Repository
					? StoreSelection.Repository
					: StoreSelection.Machine)
				.Select(heading => NoteFile.Read(heading.Path) is { } content
					? IndexedNote.Of(NoteReader.Parse(heading.Path, scope, content))
					: null)
				.OfType<IndexedNote>())
			.Notes];

	/// <summary>Whether a note needs enriching, and why.</summary>
	private (IndexedNote Note, StaleReason? Reason) State(IndexedNote note, IndexJournal journal)
	{
		var content = NoteFile.Read(note.Heading.Path);
		if (content is null) return (note, null);

		var hash = NoteFile.SourceHash(content);

		if (journal.Skips.Any(skip => Names(skip.Note, note.Heading.Name) && skip.Hash == hash))
		{
			return (note, null);
		}

		if (journal.Attempts.Any(attempt => Names(attempt.Note, note.Heading.Name)
			&& attempt.Hash == hash
			&& attempt.Failures >= RetryLimit))
		{
			return (note, null);
		}

		if (journal.Forced.Any(forced => Names(forced, note.Heading.Name))) return (note, StaleReason.Rebuild);

		var stamp = IndexStamp.Of(NoteFrontmatter.Parse(FrontmatterBlock.Split(content).Yaml));

		if (stamp is not { } carried) return (note, StaleReason.NeverIndexed);
		if (carried.Schema != IndexStamp.CurrentSchema) return (note, StaleReason.SchemaChanged);
		if (carried.Prompt != _promptHash) return (note, StaleReason.PromptChanged);

		var expected = IndexStamp.For(_promptHash, content);

		return carried.Source == expected.Source ? (note, null) : (note, StaleReason.SourceChanged);
	}

	/// <summary>Holes before staleness, because a hole is invisible and a stale gist is only wrong.</summary>
	private static int Band(StaleReason reason) => reason switch
	{
		StaleReason.NeverIndexed => 0,
		StaleReason.SourceChanged => 1,
		StaleReason.Rebuild => 2,
		StaleReason.SchemaChanged or StaleReason.PromptChanged => 3,
		_ => 4,
	};

	private IndexProgress Progress(
		IReadOnlyList<(IndexedNote Note, StaleReason? Reason)> states,
		IndexJournal journal,
		DateTimeOffset now)
	{
		var fresh = states.Count(state => state.Reason is null);
		var holes = states.Count(state => state.Reason == StaleReason.NeverIndexed);
		var stale = states.Count(state => state.Reason is not null and not StaleReason.NeverIndexed);

		return new IndexProgress
		{
			Total = states.Count,
			Fresh = fresh,
			NeverIndexed = holes,
			Stale = stale,
			Claimed = journal.Leases.Count(lease => lease.Expires > now),
			Skipped = journal.Skips.Count,
			Failed = journal.Attempts.Count(attempt => attempt.Failures >= RetryLimit),
			Remaining = holes + stale,
		};
	}

	/// <summary>Everything the agent is handed, so it needs to read nothing else.</summary>
	private NoteWorkItem WorkItem(
		(IndexedNote Note, StaleReason? Reason) candidate,
		IndexJournal.Lease lease,
		IReadOnlyList<IndexedNote> notes,
		NoteStores stores,
		NoteScope scope)
	{
		var content = NoteFile.Read(candidate.Note.Heading.Path) ?? string.Empty;
		var block = FrontmatterBlock.Split(content);
		var matter = NoteFrontmatter.Parse(block.Yaml);

		return new NoteWorkItem
		{
			Lease = lease.Token,
			Expires = lease.Expires,
			Name = candidate.Note.Heading.Name,
			Path = candidate.Note.Heading.Path,
			Content = Stripped(content),
			Reason = candidate.Reason!.Value,
			InboundLinks = candidate.Note.InboundLinks,
			Vocabulary = Vocabulary(notes),
			Exemplars = Exemplars(notes, candidate.Note.Heading.Name),
			LinkCandidates = Candidates(stores, scope, candidate.Note),
			Existing = matter.IsEnriched ? Read(matter) : null,
		};
	}

	/// <summary>
	/// The note without the keys the indexer writes. An agent re-enriching one should be looking at
	/// the note, not at the answer it is about to replace.
	/// </summary>
	private static string Stripped(string content) =>
		FrontmatterSplice.Apply(content, Keys.ToDictionary(key => key, _ => (string?)null));

	/// <summary>The topics in use, commonest first, which is the order that makes reuse the easy choice.</summary>
	private static IReadOnlyList<TopicUse> Vocabulary(IReadOnlyList<IndexedNote> notes)
	{
		var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		foreach (var note in notes)
		{
			var content = NoteFile.Read(note.Heading.Path);
			if (content is null) continue;

			foreach (var topic in NoteFrontmatter.Parse(FrontmatterBlock.Split(content).Yaml).Sequence("dn-topics"))
			{
				counts[topic] = counts.GetValueOrDefault(topic) + 1;
			}
		}

		return [.. counts.OrderByDescending(pair => pair.Value)
			.ThenBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => new TopicUse { Name = pair.Key, Uses = pair.Value })];
	}

	/// <summary>
	/// Three accepted enrichments from this store.
	/// <para>
	/// Rotated by the target's own name rather than always the same three, so a run does not converge
	/// on copying one note -- and deterministic, so re-enriching the same note twice is handed the
	/// same examples and has no reason to answer differently.
	/// </para>
	/// </summary>
	private static IReadOnlyList<NoteExemplar> Exemplars(IReadOnlyList<IndexedNote> notes, string target)
	{
		var enriched = notes
			.Where(note => !note.Heading.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
			.Select(note => (note, content: NoteFile.Read(note.Heading.Path)))
			.Where(pair => pair.content is not null)
			.Select(pair => (pair.note, matter: NoteFrontmatter.Parse(FrontmatterBlock.Split(pair.content!).Yaml)))
			.Where(pair => pair.matter.IsEnriched)
			.OrderBy(pair => pair.note.Heading.Name, StringComparer.Ordinal)
			.ToArray();

		if (enriched.Length == 0) return [];

		var offset = Math.Abs(target.GetHashCode(StringComparison.Ordinal)) % enriched.Length;

		return [.. Enumerable.Range(0, Math.Min(3, enriched.Length))
			.Select(index => enriched[(offset + index) % enriched.Length])
			.Select(pair => new NoteExemplar
			{
				Name = pair.note.Heading.Name,
				Gist = pair.matter.Scalar("dn-gist") ?? string.Empty,
				Asks = pair.matter.Sequence("dn-asks"),
				Topics = pair.matter.Sequence("dn-topics"),
			})];
	}

	/// <summary>
	/// The notes this one could link to, found by searching the store for its own words. Supplied
	/// rather than discovered, so the same note always gets the same candidates and the turn stays
	/// one call in and one out.
	/// </summary>
	private IReadOnlyList<LinkCandidate> Candidates(NoteStores stores, NoteScope scope, IndexedNote note)
	{
		var query = $"{note.Heading.Name} {note.Heading.Description}";
		var selection = scope == NoteScope.Repository ? StoreSelection.Repository : StoreSelection.Machine;

		return [.. _search.Search(stores, new NoteQuery { Text = query, Scope = selection, Limit = 21 })
			.Where(hit => !hit.Heading.Name.Equals(note.Heading.Name, StringComparison.OrdinalIgnoreCase))
			.Take(20)
			.Select(hit => new LinkCandidate
			{
				Name = hit.Heading.Name,
				Gist = hit.Heading.Gist ?? hit.Heading.Description,
			})];
	}

	/// <summary>Where the last few writes sit against the store's own norms.</summary>
	private static DriftReport Drift(IReadOnlyList<IndexedNote> notes)
	{
		var enriched = notes
			.Select(note => NoteFile.Read(note.Heading.Path))
			.OfType<string>()
			.Select(content => NoteFrontmatter.Parse(FrontmatterBlock.Split(content).Yaml))
			.Where(matter => matter.IsEnriched)
			.ToArray();

		if (enriched.Length == 0)
		{
			return new DriftReport
			{
				Window = 0,
				MeanGistLength = 0,
				MeanAsks = 0,
				MeanTopics = 0,
				SingleUseTopics = 0,
				Warnings = ["Nothing has been enriched yet."],
			};
		}

		var topics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		foreach (var topic in enriched.SelectMany(matter => matter.Sequence("dn-topics")))
		{
			topics[topic] = topics.GetValueOrDefault(topic) + 1;
		}

		var once = topics.Count(pair => pair.Value == 1);
		var warnings = new List<string>();

		// A vocabulary that is mostly single-use topics is a vocabulary nobody can filter by, which
		// is the leading indicator of drift: it shows long before retrieval gets measurably worse.
		if (enriched.Length >= 20 && once > topics.Count / 2)
		{
			warnings.Add(
				$"{once} of {topics.Count} topics are used once. The vocabulary is being ignored; "
					+ "check the exemplars being handed out.");
		}

		return new DriftReport
		{
			Window = enriched.Length,
			MeanGistLength = Math.Round(enriched.Average(matter => (matter.Scalar("dn-gist") ?? string.Empty).Length), 1),
			MeanAsks = Math.Round(enriched.Average(matter => matter.Sequence("dn-asks").Count), 2),
			MeanTopics = Math.Round(enriched.Average(matter => matter.Sequence("dn-topics").Count), 2),
			SingleUseTopics = once,
			Warnings = warnings,
		};
	}

	/// <summary>The enrichment a note already carries.</summary>
	private static Contracts.Enrichment Read(NoteFrontmatter matter) => new()
	{
		Gist = matter.Scalar("dn-gist") ?? string.Empty,
		Asks = matter.Sequence("dn-asks"),
		Topics = matter.Sequence("dn-topics"),
		Entities = matter.Sequence("dn-entities"),
		Aliases = matter.Sequence("dn-aliases"),
		Links = matter.Sequence("dn-links"),
		Confidence = matter.Scalar("dn-confidence") ?? "medium",
	};

	/// <summary>The note with its enrichment written in, and its stamp brought up to date.</summary>
	private string Splice(
		string content,
		Contracts.Enrichment enrichment,
		IReadOnlyList<string> links,
		string storePath)
	{
		_ = storePath;

		var lineEnding = FrontmatterBlock.Split(content).LineEnding;
		var entries = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["dn-gist"] = FrontmatterSplice.Entry("dn-gist", enrichment.Gist),
			["dn-asks"] = FrontmatterSplice.Entry("dn-asks", enrichment.Asks, lineEnding),
			["dn-topics"] = FrontmatterSplice.Entry("dn-topics", enrichment.Topics, lineEnding),
			["dn-entities"] = Optional("dn-entities", enrichment.Entities, lineEnding),
			["dn-aliases"] = Optional("dn-aliases", enrichment.Aliases, lineEnding),
			["dn-links"] = Optional("dn-links", [.. links.Select(link => $"[[{link}]]")], lineEnding),
			["dn-confidence"] = FrontmatterSplice.Entry("dn-confidence", enrichment.Confidence),
		};

		// The stamp is computed against the note as it is now, before the enrichment goes in --
		// which is exactly what SourceHash measures, since it ignores these keys.
		entries[IndexStamp.Key] = FrontmatterSplice.Entry(
			IndexStamp.Key,
			IndexStamp.For(_promptHash, content).ToString());

		return FrontmatterSplice.Apply(content, entries);
	}

	private static string? Optional(string key, IReadOnlyList<string> values, string lineEnding) =>
		values.Count == 0 ? null : FrontmatterSplice.Entry(key, values, lineEnding);

	/// <summary>Whether a claim may still be honoured.</summary>
	/// <exception cref="ArgumentException">It cannot.</exception>
	private static void Verify(IndexJournal journal, string token, string name, string content, DateTimeOffset now)
	{
		var lease = journal.Leases.FirstOrDefault(candidate => candidate.Token == token);

		if (lease is null)
		{
			throw new ArgumentException(
				$"Lease '{token}' is not one this run issued. Call note_index_next for work.");
		}

		if (lease.Expires > now) return;

		// Expired, which on its own is not a reason to throw work away. What matters is whether the
		// note is still the one that was claimed.
		var stamp = IndexStamp.Of(NoteFrontmatter.Parse(FrontmatterBlock.Split(content).Yaml));
		var current = IndexStamp.For(stamp?.Prompt ?? string.Empty, content);

		if (stamp is { } carried && carried.Source != current.Source)
		{
			throw new ArgumentException(
				$"Lease for '{name}' expired at {lease.Expires:u} and the note has changed since. "
					+ "Call note_index_next again.");
		}
	}

	private static IReadOnlyList<IndexJournal.Attempt> Failed(
		IndexJournal journal,
		string name,
		string hash,
		string? reason)
	{
		var existing = journal.Attempts.FirstOrDefault(
			attempt => Names(attempt.Note, name) && attempt.Hash == hash);

		return
		[
			.. journal.Attempts.Where(attempt => !(Names(attempt.Note, name) && attempt.Hash == hash)),
			new IndexJournal.Attempt
			{
				Note = name,
				Hash = hash,
				Failures = (existing?.Failures ?? 0) + 1,
				LastError = reason,
			},
		];
	}

	/// <summary>The note a lease token is about. The token is self-describing so a stale one names itself.</summary>
	/// <exception cref="ArgumentException">The token is not one of ours.</exception>
	private static string NameOf(string token)
	{
		var parts = token.Split(':');

		if (parts.Length != 3 || parts[1].Length == 0)
		{
			throw new ArgumentException($"'{token}' is not a lease. Pass the one note_index_next returned.");
		}

		return parts[1];
	}

	private static bool Names(string left, string right) =>
		left.Equals(right, StringComparison.OrdinalIgnoreCase);

	/// <summary>Every key this mode owns, and the only keys it writes.</summary>
	private static readonly string[] Keys =
		["dn-gist", "dn-asks", "dn-topics", "dn-entities", "dn-aliases", "dn-links", "dn-confidence", IndexStamp.Key];
}

/// <summary>Which notes a rebuild puts back in the queue.</summary>
public enum RebuildSelection
{
	/// <summary>Everything already judged stale, which is what a rebuild usually means.</summary>
	Stale,

	/// <summary>The ones that failed, to try them again.</summary>
	Failed,

	/// <summary>The ones answered as not worth indexing, to reconsider.</summary>
	Skipped,

	/// <summary>Every note in the store. This discards every gist in it.</summary>
	All,
}
