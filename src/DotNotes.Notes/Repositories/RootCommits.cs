using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// The commits a repository's history starts from, read from disk without running git.
/// <para>
/// Evidence of which repository this is, and never its key. A root commit survives a move, a new
/// remote and a rename on the host, which is exactly what the naming chain does not -- but there is
/// none before the first commit, a shallow clone lacks it, a fork shares it, and a monorepo has one
/// per repository that was merged into it. So a repository's roots are a set, matched by
/// intersection, and a missed match costs no more than having no evidence at all. See
/// <c>docs/decisions/a-renamed-repository-keeps-its-notes-until-the-move-is-made.md</c>.
/// </para>
/// <para>
/// Two sources, neither of which walks history. The commit-graph lists every commit reachable from
/// every ref when it was written, with a sentinel where a commit has no parent; git writes it during
/// <c>gc</c>, so a repository old enough to have been moved usually has one. The first line of the
/// <c>HEAD</c> reflog names the initial commit of a repository made with <c>git init</c>, which covers
/// the young repository that has not been collected yet. Anything unreadable is no evidence rather
/// than a failure.
/// </para>
/// </summary>
public static class RootCommits
{
	/// <summary>What a commit-graph writes in a parent slot that holds no parent.</summary>
	private const uint NoParent = 0x70000000;

	private const uint Signature = 0x43475048; // "CGPH"
	private const uint OidLookup = 0x4F49444C; // "OIDL"
	private const uint CommitData = 0x43444154; // "CDAT"

	private const string InitialCommit = "commit (initial)";

	/// <summary>
	/// The roots each repository's graph listed, kept while the graph's stamp is unchanged. A large
	/// repository's graph is megabytes and changes only when git collects, so scanning it on every
	/// call is the cost of a whole store crawl spent on an answer that has not moved.
	/// </summary>
	private static readonly ConcurrentDictionary<string, (string Stamp, IReadOnlyList<string> Roots)> Scanned =
		new(PathCasing.Comparer);

	/// <summary>
	/// A repository's roots: the commit-graph's, a single file or a split chain, or where it lists
	/// none, the reflog's initial commit. Sorted and distinct, so a comparison with what is recorded
	/// is exact.
	/// <para>
	/// The reflog is read only when the graph has nothing. A repository young enough to have no graph
	/// is the one the reflog is for, and a graph that exists already lists the initial commit.
	/// </para>
	/// </summary>
	public static IReadOnlyList<string> Of(string commonDirectory)
	{
		var stamp = GraphStamp(commonDirectory);
		var graph = Scanned.TryGetValue(commonDirectory, out var scanned) && scanned.Stamp == stamp
			? scanned.Roots
			: Scan(commonDirectory, stamp);

		if (graph.Count > 0) return graph;

		return Initial(commonDirectory) is { } initial ? [initial] : [];
	}

	/// <summary>The roots listed by the repository's commit-graph, possibly repeated across layers.</summary>
	public static IReadOnlyList<string> InGraph(string commonDirectory)
	{
		var roots = new List<string>();

		foreach (var file in GraphFiles(commonDirectory))
		{
			roots.AddRange(FromGraph(file));
		}

		return roots;
	}

	private static IReadOnlyList<string> Scan(string commonDirectory, string stamp)
	{
		IReadOnlyList<string> roots = stamp.Length == 0
			? []
			: [.. InGraph(commonDirectory).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

		Scanned[commonDirectory] = (stamp, roots);

		return roots;
	}

	/// <summary>
	/// A cheap fingerprint of the commit-graph: the length and write time of the single file and of
	/// the chain. A layer is named by its own hash and never rewritten, so a new layer is a new
	/// chain, and two stats cover the lot. Empty where there is no graph at all.
	/// </summary>
	public static string GraphStamp(string commonDirectory)
	{
		var info = Path.Combine(commonDirectory, "objects", "info");
		var single = new FileInfo(Path.Combine(info, "commit-graph"));
		var chain = new FileInfo(Path.Combine(info, "commit-graphs", "commit-graph-chain"));

		if (!single.Exists && !chain.Exists) return string.Empty;

		var stamp = new StringBuilder();

		foreach (var file in (FileInfo[])[single, chain])
		{
			if (file.Exists) stamp.Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks);

			stamp.Append(';');
		}

		return stamp.ToString();
	}

	/// <summary>
	/// The initial commit named by the first line of the <c>HEAD</c> reflog, or null where that line
	/// is anything else -- a clone starts its reflog at the tip it fetched, and an expired log starts
	/// wherever expiry left it.
	/// </summary>
	public static string? Initial(string commonDirectory)
	{
		var path = Path.Combine(commonDirectory, "logs", "HEAD");

		try
		{
			if (!File.Exists(path)) return null;

			using var reader = new StreamReader(path);

			return InitialFrom(reader.ReadLine());
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>The commit a reflog line records as initial, or null for any other line.</summary>
	public static string? InitialFrom(string? line)
	{
		if (line is null) return null;

		var tab = line.IndexOf('\t', StringComparison.Ordinal);
		if (tab < 0 || !line.AsSpan(tab + 1).StartsWith(InitialCommit, StringComparison.Ordinal)) return null;

		var fields = line[..tab].Split(' ', 3);
		if (fields.Length < 2 || !IsZero(fields[0]) || !IsHex(fields[1])) return null;

		return fields[1].ToLowerInvariant();
	}

	/// <summary>The roots one commit-graph file lists.</summary>
	public static IReadOnlyList<string> FromGraph(string path)
	{
		try
		{
			using var handle = File.OpenHandle(path);

			return Roots(handle, RandomAccess.GetLength(handle));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			return [];
		}
	}

	/// <summary>The graph files in the order git would layer them: the single file, then the chain.</summary>
	private static IEnumerable<string> GraphFiles(string commonDirectory)
	{
		var info = Path.Combine(commonDirectory, "objects", "info");
		var single = Path.Combine(info, "commit-graph");

		if (File.Exists(single)) yield return single;

		var graphs = Path.Combine(info, "commit-graphs");
		var chain = Path.Combine(graphs, "commit-graph-chain");

		string[] layers;

		try
		{
			layers = File.Exists(chain) ? File.ReadAllLines(chain) : [];
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			layers = [];
		}

		foreach (var layer in layers)
		{
			var hash = layer.Trim();
			if (!IsHex(hash)) continue;

			var file = Path.Combine(graphs, $"graph-{hash}.graph");

			if (File.Exists(file)) yield return file;
		}
	}

	/// <summary>
	/// Every commit in one graph whose first parent slot is empty. Reads the fixed-width commit data
	/// in blocks rather than whole, and reads an object id only for the commits that qualify.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is not a commit-graph this understands.</exception>
	private static List<string> Roots(Microsoft.Win32.SafeHandles.SafeFileHandle handle, long length)
	{
		Span<byte> header = stackalloc byte[8];
		Exact(handle, header, 0);

		if (BinaryPrimitives.ReadUInt32BigEndian(header) != Signature || header[4] != 1)
		{
			throw new InvalidDataException("Not a version 1 commit-graph.");
		}

		var hashLength = header[5] switch
		{
			1 => 20,
			2 => 32,
			_ => throw new InvalidDataException("Unknown hash version."),
		};

		var chunks = Chunks(handle, header[6], length);

		if (!chunks.TryGetValue(OidLookup, out var oids) || !chunks.TryGetValue(CommitData, out var data))
		{
			throw new InvalidDataException("A commit-graph without its lookup or data chunk.");
		}

		var record = hashLength + 16;
		var count = oids.Length / hashLength;

		if (data.Length < (long)count * record) throw new InvalidDataException("Truncated commit data.");

		var roots = new List<string>();
		var block = new byte[record * 4096];
		var id = new byte[hashLength];

		for (long first = 0; first < count; first += 4096)
		{
			var inBlock = (int)Math.Min(4096, count - first);
			var span = block.AsSpan(0, inBlock * record);

			Exact(handle, span, data.Offset + (first * record));

			for (var i = 0; i < inBlock; i++)
			{
				var parent = BinaryPrimitives.ReadUInt32BigEndian(span.Slice((i * record) + hashLength, 4));
				if (parent != NoParent) continue;

				Exact(handle, id, oids.Offset + ((first + i) * hashLength));
				roots.Add(Convert.ToHexStringLower(id));
			}
		}

		return roots;
	}

	/// <summary>The chunk table: each chunk's offset, and its length from where the next one starts.</summary>
	private static Dictionary<uint, (long Offset, long Length)> Chunks(
		Microsoft.Win32.SafeHandles.SafeFileHandle handle,
		int count,
		long fileLength)
	{
		var table = new byte[(count + 1) * 12];
		Exact(handle, table, 8);

		var chunks = new Dictionary<uint, (long Offset, long Length)>();

		for (var i = 0; i < count; i++)
		{
			var entry = table.AsSpan(i * 12, 24);
			var chunk = BinaryPrimitives.ReadUInt32BigEndian(entry);
			var offset = BinaryPrimitives.ReadInt64BigEndian(entry[4..]);
			var next = BinaryPrimitives.ReadInt64BigEndian(entry[16..]);

			if (offset < 0 || next < offset || next > fileLength) throw new InvalidDataException("Chunk out of range.");

			chunks[chunk] = (offset, next - offset);
		}

		return chunks;
	}

	/// <summary>Fills a buffer from an offset, or throws where the file ends first.</summary>
	private static void Exact(Microsoft.Win32.SafeHandles.SafeFileHandle handle, Span<byte> buffer, long offset)
	{
		var read = 0;

		while (read < buffer.Length)
		{
			var got = RandomAccess.Read(handle, buffer[read..], offset + read);
			if (got == 0) throw new InvalidDataException("The file ends early.");

			read += got;
		}
	}

	private static bool IsZero(string value) => value.Length is 40 or 64 && value.All(c => c == '0');

	private static bool IsHex(string value) => value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);
}
