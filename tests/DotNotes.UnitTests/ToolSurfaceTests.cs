using System.Text.Json;

using DotNotes.Contracts;

namespace DotNotes.UnitTests;

/// <summary>
/// What a session is offered, held to exactly what was meant.
/// <para>
/// A pinned list rather than a count, so adding a tool is a deliberate line in a diff. A surface
/// grows one reasonable-looking tool at a time, and every one of them is paid for at the start of
/// every session by every caller, including the ones that never use it.
/// </para>
/// </summary>
public sealed class ToolSurfaceTests
{
	/// <summary>The surface, in full.</summary>
	private static readonly string[] Expected =
	[
		ToolNames.Check,
		ToolNames.Context,
		ToolNames.Delete,
		ToolNames.Move,
		ToolNames.Read,
		ToolNames.Search,
		ToolNames.Write,
	];

	/// <summary>The ones that promise to change nothing.</summary>
	private static readonly string[] Reading =
	[
		ToolNames.Check,
		ToolNames.Context,
		ToolNames.Read,
		ToolNames.Search,
	];

	/// <summary>
	/// The ones a client should confirm. Deleting leaves nothing behind; moving can publish a
	/// private note to everyone who clones, and moving it back does not unsend it.
	/// </summary>
	private static readonly string[] Destructive = [ToolNames.Delete, ToolNames.Move];

	[Test]
	public void The_server_offers_exactly_the_listed_tools() =>
		Surface.Listed().Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal)
			.ShouldBe(Expected.OrderBy(name => name, StringComparer.Ordinal));

	[Test]
	public void Every_name_starts_with_the_server_prefix() =>
		Surface.Listed().ShouldAllBe(tool => tool.Name.StartsWith("note_", StringComparison.Ordinal));

	/// <summary>
	/// Read-only is a promise a client may act on by not asking permission, so it is stated per tool
	/// rather than inferred, and a new tool cannot arrive holding it without somebody adding it here.
	/// </summary>
	[Test]
	public void Only_the_reading_tools_call_themselves_read_only() =>
		Surface.Listed().Where(tool => tool.Annotations?.ReadOnlyHint == true).Select(tool => tool.Name)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ShouldBe(Reading.OrderBy(name => name, StringComparer.Ordinal));

	/// <summary>
	/// Writing is not destructive and the other two are. A write is addressed by name and what it
	/// replaced is in git or in the vault's history, so a confirmation in front of the commonest
	/// operation would only teach the user to click through the ones that matter -- which are
	/// deleting, which leaves nothing, and moving, which can publish a private note.
	/// </summary>
	[Test]
	public void Only_the_operations_that_cannot_be_undone_call_themselves_destructive() =>
		Surface.Listed().Where(tool => tool.Annotations?.DestructiveHint == true).Select(tool => tool.Name)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ShouldBe(Destructive.OrderBy(name => name, StringComparer.Ordinal));

	/// <summary>
	/// The output schema is a third of what a listing costs, and carries no prose at all -- a model
	/// that read one would learn field names and nothing about what they mean.
	/// </summary>
	[Test]
	public void The_listing_carries_no_output_schema() =>
		Surface.Listed().ShouldAllBe(tool => tool.OutputSchema == null);

	/// <summary>
	/// Descriptions are raw string literals in CRLF files, so every break in one reaches the client
	/// as a carriage return, and at least one client passes those to the model verbatim.
	/// </summary>
	[Test]
	public void The_listing_carries_no_carriage_return()
	{
		foreach (var tool in Surface.Listed())
		{
			(tool.Description ?? string.Empty).ShouldNotContain("\r", customMessage: tool.Name);
			tool.InputSchema.GetRawText().ShouldNotContain("\\r", customMessage: tool.Name);
		}
	}

	/// <summary>
	/// MCP gives structured content one JSON object, so a tool returning a bare list has nowhere to
	/// put it. Every result is a record with named properties.
	/// </summary>
	[Test]
	public void No_tool_returns_a_bare_collection()
	{
		foreach (var tool in Surface.Listed())
		{
			if (tool.OutputSchema is not { } schema) continue;

			schema.GetProperty("type").GetString().ShouldBe("object", tool.Name);
		}
	}

	/// <summary>Every argument says what it is for; an undocumented one is guessed at.</summary>
	[Test]
	public void Every_argument_is_described()
	{
		foreach (var tool in Surface.Listed())
		{
			if (!tool.InputSchema.TryGetProperty("properties", out var properties)) continue;

			foreach (var property in properties.EnumerateObject())
			{
				property.Value.TryGetProperty("description", out var described).ShouldBeTrue(
					$"{tool.Name}'s {property.Name}");

				described.ValueKind.ShouldBe(JsonValueKind.String);
			}
		}
	}

	/// <summary>
	/// Every tool named in the instructions has to exist. Instructions that name a tool a client
	/// cannot see are worse than silence: they spend context teaching an approach that fails on the
	/// first call.
	/// </summary>
	[Test]
	public void The_instructions_name_only_tools_that_exist()
	{
		var instructions = Surface.Instructions();
		var offered = Surface.Listed().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

		foreach (var word in instructions.Split([' ', '\n', '\r', ',', '.', ':', ';'],
			StringSplitOptions.RemoveEmptyEntries))
		{
			if (word.StartsWith("note_", StringComparison.Ordinal)) offered.ShouldContain(word);
		}
	}

	/// <summary>And every tool is worth naming there, or a caller has to discover it by reading a list.</summary>
	[Test]
	public void The_instructions_name_every_tool()
	{
		var instructions = Surface.Instructions();

		foreach (var tool in Surface.Listed())
		{
			instructions.ShouldContain(tool.Name);
		}
	}
}
