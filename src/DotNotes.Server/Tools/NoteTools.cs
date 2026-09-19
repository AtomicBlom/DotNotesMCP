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

	/// <summary>
	/// Not destructive, deliberately, where <c>note_delete</c> is. A write is addressed by name and
	/// carries the whole note, and what it replaced is in git for a committed note and in the vault's
	/// own history otherwise. Marking the commonest operation destructive puts a confirmation in
	/// front of it and teaches the user to click through -- which spends the consent the hint exists
	/// to collect, on the one operation that does not need it.
	/// </summary>
	[McpServerTool(
		Name = ToolNames.Write,
		Title = "Write a note",
		ReadOnly = false,
		Destructive = false,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Write)]
	public NoteWritten Write(
		[Description(ToolDescriptions.NoteNameArgument)] string name,
		[Description(ToolDescriptions.DescriptionArgument)] string description,
		[Description(ToolDescriptions.BodyArgument)] string body,
		[Description(ToolDescriptions.WriteScopeArgument)] string scope,
		[Description(ToolDescriptions.NoteTypeArgument)] string? type = null,
		[Description(ToolDescriptions.NoteTagsArgument)] string[]? tags = null,
		[Description(ToolDescriptions.MachinesArgument)] string[]? machines = null,
		[Description(ToolDescriptions.RevisionArgument)] string? revision = null) =>
		notes.Write(name, description, body, scope, type, tags, machines, revision);

	[McpServerTool(
		Name = ToolNames.Delete,
		Title = "Delete a note",
		ReadOnly = false,
		Destructive = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Delete)]
	public NoteDeleted Delete(
		[Description(ToolDescriptions.NameArgument)] string name,
		[Description(ToolDescriptions.WriteScopeArgument)] string scope) =>
		notes.Delete(name, scope);

	/// <summary>
	/// Destructive, unlike a write, because one direction of it publishes. Moving a private note to
	/// repository scope puts it in front of everyone who clones, and moving it back does not unsend
	/// it -- so this is the operation where a confirmation is worth the interruption.
	/// </summary>
	[McpServerTool(
		Name = ToolNames.Move,
		Title = "Rename or move a note",
		ReadOnly = false,
		Destructive = true,
		Idempotent = false,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Move)]
	public NoteMoved Move(
		[Description(ToolDescriptions.NameArgument)] string name,
		[Description(ToolDescriptions.ToNameArgument)] string? toName = null,
		[Description(ToolDescriptions.ToScopeArgument)] string? toScope = null) =>
		notes.Move(name, toName, toScope);

	[McpServerTool(
		Name = ToolNames.Check,
		Title = "Find what is wrong in the stores",
		ReadOnly = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.Check)]
	public NoteCheckReport Check(
		[Description(ToolDescriptions.ScopeArgument)] string? scope = null) =>
		notes.Check(scope);

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
