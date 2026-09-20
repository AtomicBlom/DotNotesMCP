using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using DotNotes.Contracts;

using ModelContextProtocol;

namespace DotNotes.Server;

/// <summary>
/// What a tool's arguments and its result are marshalled through: the SDK's own serializer, taught
/// about this server's result shapes.
/// <para>
/// Combined rather than replaced. The SDK's resolver describes the protocol -- content blocks,
/// errors, the envelope a result travels in -- and losing any of that would break the wire format
/// rather than one tool. <see cref="ResultJson"/> is asked first and defers to it for everything it
/// does not know.
/// </para>
/// <para>
/// Both modes use this one instance. Their tool surfaces are deliberately disjoint, but that is a
/// statement about what a client is offered rather than about how bytes are written, and two
/// serializers would be two chances to disagree about the shape of a field they share.
/// </para>
/// </summary>
internal static class ToolJson
{
	/// <summary>Read-only, so it is built once rather than locked on first use per call.</summary>
	public static JsonSerializerOptions Options { get; } = Combined();

	private static JsonSerializerOptions Combined()
	{
		var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				ResultJson.Default,
				McpJsonUtilities.DefaultOptions.TypeInfoResolver),
		};

		options.MakeReadOnly();

		return options;
	}
}
