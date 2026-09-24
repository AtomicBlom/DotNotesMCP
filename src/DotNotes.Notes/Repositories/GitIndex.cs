using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace DotNotes.Notes.Repositories;

/// <summary>
/// The paths git tracks in a checkout, read from its index file without running git.
/// <para>
/// It is how a committed store is found wherever somebody moved it: the tracked paths are listed in
/// one file, so finding every <c>index.md</c> is one sequential read rather than a walk of the working
/// tree, and it is current as soon as a <c>git mv</c> is staged. Versions 2 to 4 are read; version 4
/// compresses each path against the one before it, which is why the read cannot skip ahead.
/// </para>
/// <para>
/// The answer is kept per index file and discarded when the file's length or write time changes, the
/// same stamp the search index is kept by. git replaces the index whole whenever it changes it, so
/// the stamp moves with every staged change.
/// </para>
/// </summary>
public static class GitIndex
{
	private const uint Signature = 0x44495243; // "DIRC"

	/// <summary>What precedes the hash in every entry: ctime, mtime, dev, ino, mode, uid, gid, size.</summary>
	private const int StatLength = 40;

	private const uint ModeMask = 0xF000;
	private const uint RegularFile = 0x8000;
	private const uint SymbolicLink = 0xA000;

	private static readonly ConcurrentDictionary<string, (long Length, long Written, IReadOnlyList<string> Paths)> Cache =
		new(PathCasing.Comparer);

	/// <summary>
	/// The tracked files in a checkout whose name is <paramref name="fileName"/>, as paths relative to
	/// the working tree with forward slashes. Empty where there is no index or it cannot be read:
	/// this finds stores, and an unreadable index is a store not found rather than a failure.
	/// </summary>
	/// <param name="gitDirectory">The checkout's own git directory: a linked worktree has an index of its own.</param>
	/// <param name="fileName">The file name to match, exactly.</param>
	public static IReadOnlyList<string> Tracked(string gitDirectory, string fileName)
	{
		var path = Path.Combine(gitDirectory, "index");

		try
		{
			var info = new FileInfo(path);
			if (!info.Exists) return [];

			var written = info.LastWriteTimeUtc.Ticks;
			var key = $"{path}|{fileName}";

			if (Cache.TryGetValue(key, out var cached) && cached.Length == info.Length && cached.Written == written)
			{
				return cached.Paths;
			}

			var paths = Named(File.ReadAllBytes(path), fileName);

			Cache[key] = (info.Length, written, paths);

			return paths;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	/// <summary>
	/// The entries of an index file named <paramref name="fileName"/>. The hash length is not recorded
	/// in the file, so it is tried as SHA-1 and then SHA-256; every entry carries its own path length,
	/// which the wrong guess contradicts at the first entry.
	/// </summary>
	public static IReadOnlyList<string> Named(byte[] index, string fileName)
	{
		foreach (var hashLength in (int[])[20, 32])
		{
			if (Entries(index, hashLength, fileName) is { } paths) return paths;
		}

		return [];
	}

	/// <summary>The matching entries, or null where the file does not parse with this hash length.</summary>
	private static List<string>? Entries(byte[] index, int hashLength, string fileName)
	{
		if (index.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(index) != Signature) return null;

		var version = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(4));
		if (version is < 2 or > 4) return null;

		var count = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(8));
		var suffix = Encoding.UTF8.GetBytes("/" + fileName);
		var whole = Encoding.UTF8.GetBytes(fileName);
		var matches = new List<string>();
		var previous = Array.Empty<byte>();
		var offset = 12;

		for (var i = 0; i < count; i++)
		{
			var start = offset;
			var name = start + StatLength + hashLength + 2;

			if (name > index.Length) return null;

			var mode = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(start + 24));
			var flags = BinaryPrimitives.ReadUInt16BigEndian(index.AsSpan(name - 2));

			if (version >= 3 && (flags & 0x4000) != 0) name += 2;

			var declared = flags & 0xFFF;
			byte[] path;

			if (version == 4)
			{
				if (!Varint(index, ref name, out var strip) || strip > previous.Length) return null;

				var end = Array.IndexOf(index, (byte)0, name);
				if (end < 0) return null;

				path = [.. previous.AsSpan(0, previous.Length - (int)strip), .. index.AsSpan(name, end - name)];
				offset = end + 1;
			}
			else
			{
				var end = Array.IndexOf(index, (byte)0, name);
				if (end < 0) return null;

				path = index[name..end];

				// Entries are padded with one to eight NULs to a multiple of eight bytes from their start.
				offset = start + ((end - start + 8) & ~7);
			}

			// No entry has an empty path, and every entry says how long its path is; a wrong guess at
			// the hash length reads its flags out of the hash and contradicts one or the other.
			if (path.Length == 0 || (declared < 0xFFF && path.Length != declared)) return null;

			previous = path;

			// A sparse index lists a directory outside the cone as one entry, and a submodule is a
			// gitlink; neither is a file in this working tree.
			var file = (mode & ModeMask) is RegularFile or SymbolicLink;

			if (file && (path.AsSpan().EndsWith(suffix) || path.AsSpan().SequenceEqual(whole)))
			{
				matches.Add(Encoding.UTF8.GetString(path));
			}
		}

		return matches;
	}

	/// <summary>
	/// git's offset varint: seven bits a byte, most significant first, with one added before each
	/// continuation so that no value has two encodings.
	/// </summary>
	private static bool Varint(byte[] data, ref int offset, out long value)
	{
		value = 0;

		if (offset >= data.Length) return false;

		var current = data[offset++];
		value = current & 0x7F;

		while ((current & 0x80) != 0)
		{
			if (offset >= data.Length || value > (long.MaxValue >> 8)) return false;

			current = data[offset++];
			value = ((value + 1) << 7) | (long)(current & 0x7F);
		}

		return true;
	}
}
