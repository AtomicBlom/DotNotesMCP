using System.Text.Json;
using System.Text.Json.Serialization;

using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;

namespace DotNotes.Index.Enrichment;

/// <summary>
/// The only durable state the indexing mode keeps, and only while a run is in progress.
/// <para>
/// What a note says about itself lives in the note -- its enrichment, and the schema, prompt and
/// content it was written against. That is what makes a machine which has never indexed able to
/// tell fresh from stale, and what makes a crash between writing the note and updating this file
/// need no recovery at all: the next crawl adopts what the note already says.
/// </para>
/// <para>
/// So this holds only what is about a run rather than about a note: who is holding what, what has
/// failed, and what has been answered as not worth doing. Machine-local, beside the settings and
/// the locks, never inside a store -- a synced vault would replicate a lease to the other machine
/// minutes late, where it would be indistinguishable from a live one.
/// </para>
/// </summary>
public sealed record IndexJournal
{
	// Settable rather than init-only, and that is a serialization requirement rather than a style
	// choice. The JSON source generator models an init-only member as a constructor parameter and
	// assigns every one of them at once, so a key the file omits arrives as default -- turning each
	// empty list below into null, which is a NullReferenceException in whichever run reads a
	// journal written before one of these existed. A settable property gets a setter the
	// deserializer calls only for keys that are present. Nothing mutates a journal after it is read;
	// WithoutExpired returns a new one.

	public IReadOnlyList<Lease> Leases { get; set; } = [];

	public IReadOnlyList<Attempt> Attempts { get; set; } = [];

	public IReadOnlyList<Skip> Skips { get; set; } = [];

	/// <summary>
	/// Notes put back in the queue on purpose. Recorded here rather than by stripping each note's
	/// enrichment, because rewriting several hundred files to mark them stale is a full sync for a
	/// decision that may be reversed by the next run.
	/// </summary>
	public IReadOnlyList<string> Forced { get; set; } = [];

	/// <summary>The journal for a store, or an empty one.</summary>
	public static IndexJournal Read(string storePath, string? localAppData)
	{
		var path = PathFor(storePath, localAppData);

		try
		{
			if (!File.Exists(path)) return new IndexJournal();

			return JsonSerializer.Deserialize(File.ReadAllText(path), IndexJournalJson.Default.IndexJournal)
				?? new IndexJournal();
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			// Unlike a config file, this one decides nothing about where data goes. Losing it costs
			// a run its leases and its retry counts, which the next call rebuilds; refusing to index
			// because a transient file will not parse would be the worse trade.
			return new IndexJournal();
		}
	}

	/// <summary>Writes the journal whole, under whatever lock the caller is already holding.</summary>
	public void Write(string storePath, string? localAppData)
	{
		var path = PathFor(storePath, localAppData);

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, JsonSerializer.Serialize(this, IndexJournalJson.Default.IndexJournal));
	}

	/// <summary>Named for the store it covers, folded so two spellings of one store share it.</summary>
	public static string PathFor(string storePath, string? localAppData) =>
		Path.Combine(
			MachineSettingsFile.DirectoryFor(localAppData),
			"index",
			$"{CanonicalPath.Hash(storePath)}.json");

	/// <summary>The journal with every expired lease dropped.</summary>
	public IndexJournal WithoutExpired(DateTimeOffset now) =>
		this with { Leases = [.. Leases.Where(lease => lease.Expires > now)] };

	/// <summary>Whether a note is held by somebody.</summary>
	public bool IsHeld(string name, DateTimeOffset now) =>
		Leases.Any(lease => lease.Expires > now
			&& lease.Note.Equals(name, StringComparison.OrdinalIgnoreCase));

	/// <summary>One agent's hold on one note.</summary>
	public sealed record Lease
	{
		public required string Note { get; init; }

		/// <summary>The token the agent presents. Self-describing, so a stale one is diagnosable.</summary>
		public required string Token { get; init; }

		public required string Run { get; init; }

		public required DateTimeOffset Taken { get; init; }

		public required DateTimeOffset Expires { get; init; }
	}

	/// <summary>
	/// A note that could not be enriched, and how often. Keyed by the content it failed against, so
	/// editing the note gives it a fresh start rather than inheriting a verdict about other text.
	/// </summary>
	public sealed record Attempt
	{
		public required string Note { get; init; }

		public required string Hash { get; init; }

		public required int Failures { get; init; }

		public string? LastError { get; init; }
	}

	/// <summary>A note answered as needing no enrichment, for the content it was answered about.</summary>
	public sealed record Skip
	{
		public required string Note { get; init; }

		public required string Hash { get; init; }

		public required string Reason { get; init; }
	}
}

/// <summary>The journal's shape, generated rather than discovered by reflection.</summary>
[JsonSourceGenerationOptions(
	JsonSerializerDefaults.Web,
	WriteIndented = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IndexJournal))]
internal sealed partial class IndexJournalJson : JsonSerializerContext;
