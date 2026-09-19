using DotNotes.Notes.Configuration;
using DotNotes.Server;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotNotes.UnitTests;

/// <summary>
/// The tools as a client is sent them.
/// <para>
/// Built through the real registration with the real listing trim applied, because a budget
/// measured against anything else is a number about something nobody is sent.
/// </para>
/// </summary>
public static class Surface
{
	/// <summary>Every tool a session is offered, trimmed.</summary>
	public static Tool[] Listed()
	{
		var services = new ServiceCollection();

		services.AddDotNotes(new NoteOptions());

		using var provider = services.BuildServiceProvider();

		var tools = provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool).ToArray();

		foreach (var tool in tools) ToolListing.Trim(tool);

		return tools;
	}

	/// <summary>What an indexing session is offered, which is a different surface entirely.</summary>
	public static Tool[] IndexListed()
	{
		var services = new ServiceCollection();

		services.AddDotNotesIndexing(new NoteOptions(), DotNotes.Contracts.NoteScope.Machine);

		using var provider = services.BuildServiceProvider();

		var tools = provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool).ToArray();

		foreach (var tool in tools) ToolListing.Trim(tool);

		return tools;
	}

	/// <summary>One tool by name, for a test that is about that tool.</summary>
	public static Tool Named(string name) =>
		Listed().Single(tool => tool.Name == name);

	/// <summary>The instructions a client reads during initialize.</summary>
	public static string Instructions()
	{
		var services = new ServiceCollection();

		services.AddDotNotes(new NoteOptions());

		using var provider = services.BuildServiceProvider();

		return provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<McpServerOptions>>()
			.Value.ServerInstructions ?? string.Empty;
	}
}
