using System.Text.Json.Serialization;

namespace DotNotes.Contracts;

/// <summary>
/// Every shape a tool returns, described ahead of time rather than discovered by reflection.
/// <para>
/// The MCP SDK serializes a tool's return value through its own options, whose resolver is a
/// generated context that knows the protocol's types and nothing about these. Without this the
/// server compiles with no warning at all and then fails at startup, naming the first result type
/// it cannot describe -- because the SDK builds each tool's schema while the host is starting.
/// Loud and immediate, which is the one mercy in it.
/// </para>
/// <para>
/// Only the roots are listed. Source generation walks each one's properties, so the records they
/// carry -- a match, a link, a problem, a lease -- come along without being named here, and a new
/// field on an existing result needs no edit to this file. A new <em>tool</em> does, and the surface
/// test that pins the tool list is what makes that hard to forget.
/// </para>
/// <para>
/// Property naming is left to whichever options this resolver is combined into, which is the SDK's.
/// Enums are not, and that asymmetry is the trap: a converter is chosen when the metadata is
/// generated, not when it is used, so an enum here would go out as its integer however the
/// surrounding options are configured. The reflecting path applies the SDK's string converter and
/// this one cannot, which makes it a difference that appears only in an ahead-of-time build --
/// <c>"scope": 1</c> where every other build sends <c>"scope": "Repository"</c>, with nothing
/// failing and no warning anywhere.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(NoteSearchResult))]
[JsonSerializable(typeof(NoteContent))]
[JsonSerializable(typeof(NoteContextResult))]
[JsonSerializable(typeof(NoteWritten))]
[JsonSerializable(typeof(NoteDeleted))]
[JsonSerializable(typeof(NoteMoved))]
[JsonSerializable(typeof(NoteCheckReport))]
[JsonSerializable(typeof(NoteAssignment))]
[JsonSerializable(typeof(EnrichmentAccepted))]
[JsonSerializable(typeof(SkipRecorded))]
[JsonSerializable(typeof(IndexStatus))]
public sealed partial class ResultJson : JsonSerializerContext;
