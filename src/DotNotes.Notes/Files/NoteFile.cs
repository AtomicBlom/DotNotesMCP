using System.Security.Cryptography;
using System.Text;

namespace DotNotes.Notes.Files;

/// <summary>
/// Reading and writing one note's file.
/// <para>
/// Every write is whole-file, to a temporary name in the same directory, then moved over. A sync
/// service replicating a file mid-write is the failure this prevents, and on a synced vault that is
/// not a rare case: the move is what makes the other machine see either the old note or the new one.
/// </para>
/// </summary>
public static class NoteFile
{
	private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

	/// <summary>A note's text, or null where there is no file.</summary>
	public static string? Read(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path, Utf8) : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>
	/// Writes a note, and answers whether anything changed.
	/// <para>
	/// A write whose content already matches does not happen at all. On a synced store every write
	/// is a replication and a stored revision, so re-indexing a corpus that has not changed would
	/// otherwise cost a full sync and fill the version history with identical copies.
	/// </para>
	/// </summary>
	public static bool Write(string path, string content)
	{
		if (Read(path) is { } existing && string.Equals(existing, content, StringComparison.Ordinal))
		{
			return false;
		}

		var directory = Path.GetDirectoryName(path);
		if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

		// Beside the target rather than in the system temp directory, so the move is a rename within
		// one volume. Across volumes it is a copy, which is the partial write this exists to avoid.
		//
		// The name is unique per call, not per process. Two writes to one note are serialised by the
		// store lock, but the lock lives outside the store and two callers that disagreed about where
		// it lives would both proceed -- and a shared temporary name turns that into one write
		// destroying the other's file mid-copy, rather than one of them simply winning.
		var temporary = Path.Combine(
			directory ?? ".",
			$".{Path.GetFileName(path)}.{Guid.NewGuid():n}.tmp");

		try
		{
			File.WriteAllText(temporary, content, Utf8);
			File.Move(temporary, path, overwrite: true);
		}
		catch
		{
			Delete(temporary);
			throw;
		}

		return true;
	}

	/// <summary>Removes a note, answering whether there was one.</summary>
	public static bool Delete(string path)
	{
		try
		{
			if (!File.Exists(path)) return false;

			File.Delete(path);

			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>
	/// What a caller quotes back to prove it has seen the note it is replacing. Short, because it
	/// travels on every read and every write.
	/// </summary>
	public static string Revision(string content) => Hash(content)[..12];

	/// <summary>
	/// The identity of a note's content, with line endings normalised.
	/// <para>
	/// Normalised because the same note is CRLF in a repository checkout, under this repository's
	/// own <c>eol=crlf</c> attribute, and LF wherever git stored it. Hashing the bytes would make a
	/// note's revision depend on which machine checked it out, and every clone would see every note
	/// as changed.
	/// </para>
	/// </summary>
	public static string Hash(string content)
	{
		var normalised = content.ReplaceLineEndings("\n");

		return Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(normalised)));
	}

	/// <summary>
	/// The hash the indexing mode judges staleness by: the note without the keys the indexer itself
	/// writes.
	/// <para>
	/// Excluding them is not an optimisation. Hash the whole file and writing the enrichment changes
	/// the hash of the note just enriched, so it is stale the moment it is finished and the loop
	/// never terminates.
	/// </para>
	/// </summary>
	public static string SourceHash(string content)
	{
		var block = FrontmatterBlock.Split(content);

		if (!block.Present) return Hash(content);

		var kept = new StringBuilder();

		foreach (var entry in TopLevelEntries(block.Yaml))
		{
			if (!entry.StartsWith(NoteFrontmatter.ServerPrefix, StringComparison.Ordinal))
			{
				kept.Append(entry).Append('\n');
			}
		}

		return Hash(kept + "\n" + block.Body);
	}

	/// <summary>Each top-level entry of a YAML block, with the lines it owns, keyed by its first line.</summary>
	private static IEnumerable<string> TopLevelEntries(string yaml)
	{
		var lines = yaml.Split('\n');
		var current = new StringBuilder();

		foreach (var raw in lines)
		{
			var line = raw.TrimEnd('\r');
			var continues = line.Length == 0 || char.IsWhiteSpace(line[0]);

			if (!continues && current.Length > 0)
			{
				yield return current.ToString();
				current.Clear();
			}

			current.Append(current.Length > 0 ? "\n" : string.Empty).Append(line);
		}

		if (current.Length > 0) yield return current.ToString();
	}
}
