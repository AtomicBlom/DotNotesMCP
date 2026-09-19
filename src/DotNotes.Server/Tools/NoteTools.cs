using System.ComponentModel;

using DotNotes.Contracts;

using ModelContextProtocol.Server;

namespace DotNotes.Server.Tools;

/// <summary>
/// Reading the stores. Every method is one call into <see cref="NoteService"/> and a return; the
/// logic, and the attribution every result carries, live there.
/// </summary>
[McpServerToolType]
public sealed class NoteTools(NoteService notes)
{
	[McpServerTool(
		Name = ToolNames.Search,
		Title = "Find notes",
		ReadOnly = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Search)]
	public NoteSearchResult Search(
		[Description(ToolDescriptions.QueryArgument)] string? query = null,
		[Description(ToolDescriptions.ScopeArgument)] string? scope = null,
		[Description(ToolDescriptions.TypeArgument)] string? type = null,
		[Description(ToolDescriptions.TagsArgument)] string[]? tags = null,
		[Description(ToolDescriptions.LimitArgument)] int limit = 10) =>
		notes.Search(query, scope, type, tags, limit);

	[McpServerTool(
		Name = ToolNames.Read,
		Title = "Read a note",
		ReadOnly = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Read)]
	public NoteContent Read(
		[Description(ToolDescriptions.NameArgument)] string name,
		[Description(ToolDescriptions.ScopeArgument)] string? scope = null) =>
		notes.Read(name, scope);

	[McpServerTool(
		Name = ToolNames.Context,
		Title = "Which repository, and where the stores are",
		ReadOnly = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Context)]
	public NoteContextResult Context(
		[Description(ToolDescriptions.DirectoryArgument)] string? directory = null) =>
		notes.Context(directory);
}
