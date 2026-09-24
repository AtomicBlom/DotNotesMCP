using System.Buffers.Binary;
using System.Text;

namespace DotNotes.UnitTests;

/// <summary>
/// A git index written byte by byte, in git's documented layout, so the reader is tested against the
/// format rather than against whatever index the machine running the suite happens to have.
/// </summary>
public static class GitIndexFile
{
	/// <summary>A regular file, 100644.</summary>
	public const uint File = 0x81A4;

	/// <summary>A gitlink, which is how a submodule appears in its superproject's index.</summary>
	public const uint Gitlink = 0xE000;

	/// <summary>An index listing these paths, sorted as git sorts them.</summary>
	public static byte[] Build(IEnumerable<(string Path, uint Mode)> entries, int version = 2, int hashLength = 20)
	{
		var sorted = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();

		using var stream = new MemoryStream();
		Span<byte> word = stackalloc byte[4];

		stream.Write("DIRC"u8);
		BinaryPrimitives.WriteUInt32BigEndian(word, (uint)version);
		stream.Write(word);
		BinaryPrimitives.WriteUInt32BigEndian(word, (uint)sorted.Length);
		stream.Write(word);

		var previous = Array.Empty<byte>();

		foreach (var (path, mode) in sorted)
		{
			var start = stream.Position;
			var name = Encoding.UTF8.GetBytes(path);
			var stat = new byte[40];

			BinaryPrimitives.WriteUInt32BigEndian(stat.AsSpan(24), mode);
			stream.Write(stat);
			stream.Write(new byte[hashLength]);

			var flags = new byte[2];
			BinaryPrimitives.WriteUInt16BigEndian(flags, (ushort)Math.Min(name.Length, 0xFFF));
			stream.Write(flags);

			if (version == 4)
			{
				var common = 0;
				while (common < previous.Length && common < name.Length && previous[common] == name[common]) common++;

				stream.Write(Varint(previous.Length - common));
				stream.Write(name.AsSpan(common));
				stream.WriteByte(0);
			}
			else
			{
				stream.Write(name);

				var length = stream.Position - start;
				var padded = (length + 8) & ~7;

				stream.Write(new byte[padded - length]);
			}

			previous = name;
		}

		stream.Write(new byte[hashLength]);

		return stream.ToArray();
	}

	/// <summary>Writes an index into a checkout's git directory.</summary>
	public static void Write(string checkout, params string[] paths) =>
		System.IO.File.WriteAllBytes(
			Path.Combine(checkout, ".git", "index"),
			Build(paths.Select(path => (path, File))));

	/// <summary>git's offset varint, the inverse of what the reader decodes.</summary>
	private static byte[] Varint(int value)
	{
		var bytes = new List<byte> { (byte)(value & 0x7F) };

		while ((value >>= 7) != 0)
		{
			value--;
			bytes.Insert(0, (byte)(0x80 | (value & 0x7F)));
		}

		return [.. bytes];
	}
}
