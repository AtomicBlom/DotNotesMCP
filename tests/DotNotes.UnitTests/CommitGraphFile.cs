using System.Buffers.Binary;

namespace DotNotes.UnitTests;

/// <summary>
/// A commit-graph written byte by byte, in git's documented layout, so the reader is tested against
/// the format rather than against a repository that happens to be on the machine running the suite.
/// </summary>
public static class CommitGraphFile
{
	private const uint NoParent = 0x70000000;

	/// <summary>
	/// A graph holding each root and one child of it. Commits are sorted by id, as git sorts them,
	/// and a child's parent is the position of its root in that order.
	/// </summary>
	/// <param name="path">Where to write it.</param>
	/// <param name="roots">The parentless commits, as hex.</param>
	/// <param name="hashLength">20 for SHA-1, 32 for SHA-256.</param>
	public static void Write(string path, IReadOnlyList<string> roots, int hashLength = 20)
	{
		var commits = new List<(string Id, string? Parent)>();

		foreach (var root in roots)
		{
			commits.Add((root, null));
			commits.Add((Child(root, hashLength), root));
		}

		commits.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

		var position = commits.Select((commit, index) => (commit.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
		var count = commits.Count;

		var fanout = new byte[256 * 4];
		var lookup = new byte[count * hashLength];
		var data = new byte[count * (hashLength + 16)];

		for (var i = 0; i < count; i++)
		{
			var id = Convert.FromHexString(commits[i].Id);
			id.CopyTo(lookup, i * hashLength);

			var record = data.AsSpan(i * (hashLength + 16));
			var parent = commits[i].Parent is { } named ? (uint)position[named] : NoParent;

			BinaryPrimitives.WriteUInt32BigEndian(record[hashLength..], parent);
			BinaryPrimitives.WriteUInt32BigEndian(record[(hashLength + 4)..], NoParent);
		}

		for (var bucket = 0; bucket < 256; bucket++)
		{
			var upTo = commits.Count(commit => Convert.FromHexString(commit.Id)[0] <= bucket);

			BinaryPrimitives.WriteUInt32BigEndian(fanout.AsSpan(bucket * 4), (uint)upTo);
		}

		byte[][] chunks = [fanout, lookup, data];
		uint[] ids = [0x4F494446, 0x4F49444C, 0x43444154]; // OIDF, OIDL, CDAT

		using var stream = new MemoryStream();
		Span<byte> word = stackalloc byte[8];

		stream.Write("CGPH"u8);
		stream.Write([1, hashLength == 20 ? (byte)1 : (byte)2, (byte)chunks.Length, 0]);

		long offset = 8 + ((chunks.Length + 1) * 12);

		for (var i = 0; i <= chunks.Length; i++)
		{
			BinaryPrimitives.WriteUInt32BigEndian(word, i < chunks.Length ? ids[i] : 0);
			stream.Write(word[..4]);
			BinaryPrimitives.WriteInt64BigEndian(word, offset);
			stream.Write(word);

			if (i < chunks.Length) offset += chunks[i].Length;
		}

		foreach (var chunk in chunks) stream.Write(chunk);

		stream.Write(new byte[hashLength]);

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, stream.ToArray());
	}

	/// <summary>A child's id: the root's with its first digit changed, so every root's child is distinct.</summary>
	private static string Child(string root, int hashLength)
	{
		var padded = root.PadRight(hashLength * 2, '0');
		var first = padded[0] == 'c' ? 'd' : 'c';

		return first + padded[1..];
	}
}
