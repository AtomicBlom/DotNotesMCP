using System.Text.Json.Nodes;

using DotNotes.Index;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;
using DotNotes.Server.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModelContextProtocol.Protocol;

namespace DotNotes.Server;

/// <summary>The single registration path, and the text a client reads before it does anything.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Sent to the client during initialize, which means it is read before the model decides how to
	/// approach anything. That makes it the highest-leverage text here: a tool description is read
	/// only once that tool is already a candidate, while this is what stops the reflex to reconstruct
	/// a decision from the files.
	/// <para>
	/// What earns a place is what no per-tool description can carry, because it is about the server
	/// rather than about one tool: that notes are keyed to the repository so worktrees share them,
	/// that there is no setup call, and what is worth writing down at all. The scope decision, which
	/// is the one with no undo, is deliberately not here -- it belongs on the <c>scope</c> argument
	/// of the tool that writes, where it is read at the moment it is being made rather than loaded
	/// into every session that never writes a note.
	/// </para>
	/// </summary>
	private const string Instructions = """
		Durable notes for this repository, keyed to the repository so every worktree shares one set.
		Markdown the user also reads and edits in Obsidian.

		- note_search before reconstructing a decision from files, or asking what may already be
		  answered. No query lists everything.
		- note_read one whole, with the notes that link to it.
		- note_context when an answer looks wrong: which repository resolved, and where the stores are.

		A note is what was learned last time; the code only shows what was done. Worth writing down:
		a gotcha that cost a debugging cycle, a decision and what it beat, a command nobody could
		guess, a correction the user made. Not what the code or git history already says.

		No setup call -- every tool resolves the repository from where the session is working.
		""";

	/// <summary>
	/// Registers the note tools and everything they need.
	/// <para>
	/// A method rather than lines in <c>Program</c>, because the surface tests build it too. What a
	/// client is offered and what a test measures have to be the same thing, or the budget is a
	/// number about something nobody is sent.
	/// </para>
	/// </summary>
	public static IMcpServerBuilder AddDotNotes(this IServiceCollection services, NoteOptions options)
	{
		services.AddSingleton(options);

		// TryAdd, so a host that has a better index registers it first and this does not overwrite
		// it. The seam exists for exactly one future: a persisted index, once a crawl is something a
		// caller can feel.
		services.TryAddSingleton<INoteSearch>(_ => new CrawlingNoteSearch(options));
		services.AddSingleton<NoteService>();

		return services
			.AddMcpServer(server =>
			{
				server.ServerInfo = new() { Name = "dotnotes", Version = Version };
				server.ServerInstructions = Instructions;
			})
			.WithTools<NoteTools>()
			.WithCallOrigin()
			.WithToolErrorMessages()
			.WithLeanListing();
	}

	/// <summary>What the client is told during initialize. MinVer stamps it from the git tag.</summary>
	private static string Version =>
		typeof(ServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

	/// <summary>
	/// Picks the calling directory out of <c>_meta</c> for the length of the call. A filter rather
	/// than an argument on every tool, so none of them declares it and none can forget it.
	/// </summary>
	private static IMcpServerBuilder WithCallOrigin(this IMcpServerBuilder builder) =>
		builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
		{
			using var origin = CallOrigin.Use(OriginDirectory(context.Params));

			return await next(context, cancellationToken);
		}));

	/// <summary>
	/// The origin directory a client sent, ignoring anything malformed. A client may send whatever it
	/// likes here, and a bad value has to fall back to the working directory rather than fail a call
	/// that would otherwise have worked.
	/// </summary>
	private static string? OriginDirectory(CallToolRequestParams? parameters)
	{
		if (parameters?.Meta?[CallOrigin.MetaKey] is not JsonValue value) return null;

		return value.TryGetValue(out string? directory) && Directory.Exists(directory) ? directory : null;
	}

	/// <summary>Applies the listing trim where every listing passes, so no tool can skip it.</summary>
	private static IMcpServerBuilder WithLeanListing(this IMcpServerBuilder builder) =>
		builder.WithRequestFilters(filters => filters.AddListToolsFilter(next => async (context, cancellationToken) =>
		{
			var result = await next(context, cancellationToken);

			foreach (var tool in result.Tools) ToolListing.Trim(tool);

			return result;
		}));
}
