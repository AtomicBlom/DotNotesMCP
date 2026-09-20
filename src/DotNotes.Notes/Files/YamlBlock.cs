using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace DotNotes.Notes.Files;

/// <summary>
/// A frontmatter block, read as the events YAML is made of rather than deserialized into a type.
/// <para>
/// The parser is still YamlDotNet's, and it is still a real YAML parser -- this reads the same
/// quoting, block scalars and flow sequences a person may have typed. What it does not do is ask a
/// deserializer to map those events onto CLR types, because that step is the one that works by
/// reflection: it is the reason <c>DeserializerBuilder</c> carries <c>RequiresDynamicCode</c>, and
/// so the reason a frontmatter block could not be read at all in an ahead-of-time build.
/// </para>
/// <para>
/// It also happens to be the shape this server wants. A note's frontmatter has no schema -- it is
/// whatever the person wrote, including keys nothing here has ever heard of -- so mapping it onto
/// declared types was never going to fit, and the generated static deserializer that would satisfy
/// the analyzer needs exactly those declared types.
/// </para>
/// </summary>
internal static class YamlBlock
{
	/// <summary>
	/// Every top-level key and its value. Scalars come back as text, sequences as lists, and a
	/// nested mapping as another dictionary.
	/// </summary>
	/// <exception cref="YamlException">The block is not a mapping, or is not YAML at all.</exception>
	public static Dictionary<string, object?> Read(string yaml)
	{
		var parser = new Parser(new StringReader(yaml));

		parser.Consume<StreamStart>();

		if (parser.Accept<StreamEnd>(out _)) return [];

		parser.Consume<DocumentStart>();

		if (!parser.Accept<MappingStart>(out _)) throw Unexpected(parser, "frontmatter is a mapping of keys to values");

		return ReadMapping(parser);
	}

	private static Dictionary<string, object?> ReadMapping(IParser parser)
	{
		parser.Consume<MappingStart>();

		// Ordinal, and last-wins on a repeated key. A note is data rather than configuration, so a
		// person who has typed one key twice should lose that key's earlier value rather than the
		// whole block -- which is what refusing the document would cost them.
		var values = new Dictionary<string, object?>(StringComparer.Ordinal);

		while (!parser.TryConsume<MappingEnd>(out _))
		{
			var key = parser.Consume<Scalar>().Value;

			values[key] = ReadNode(parser);
		}

		return values;
	}

	private static List<object?> ReadSequence(IParser parser)
	{
		parser.Consume<SequenceStart>();

		var items = new List<object?>();

		while (!parser.TryConsume<SequenceEnd>(out _)) items.Add(ReadNode(parser));

		return items;
	}

	private static object? ReadNode(IParser parser)
	{
		if (parser.TryConsume<Scalar>(out var scalar)) return Text(scalar);
		if (parser.Accept<SequenceStart>(out _)) return ReadSequence(parser);
		if (parser.Accept<MappingStart>(out _)) return ReadMapping(parser);

		throw Unexpected(parser, "a value here is text, a list, or a block of its own");
	}

	/// <summary>
	/// A scalar's text, or null where the key was written with nothing after it.
	/// <para>
	/// Every other value stays exactly as typed, which is the read half of the rule
	/// <see cref="YamlScalar"/> keeps on write. YAML would otherwise retype a bare <c>no</c>,
	/// <c>on</c> or <c>10</c>, and because every accessor here asks for a string, a retyped value
	/// does not read as the wrong type -- it reads as absent.
	/// </para>
	/// <para>
	/// Only an unquoted empty scalar is null. <c>key: ""</c> is a person writing an empty string and
	/// says so by quoting it.
	/// </para>
	/// </summary>
	private static string? Text(Scalar scalar) =>
		scalar.Style == ScalarStyle.Plain && scalar.Value.Length == 0 ? null : scalar.Value;

	private static YamlException Unexpected(IParser parser, string wanted)
	{
		var current = parser.Current;

		return current is null
			? new YamlException(wanted)
			: new YamlException(current.Start, current.End, wanted);
	}
}
