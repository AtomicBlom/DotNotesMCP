using System.ComponentModel;

using DotNotes.Contracts;
using DotNotes.Index.Enrichment;
using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;

using ModelContextProtocol.Server;

namespace DotNotes.Index.Tools;

/// <summary>
/// The indexing loop, and the whole tool surface of <c>--mode index</c>.
/// <para>
/// An agent claims a note, enriches it, submits it, and repeats. The server owns durability,
/// ordering and progress; the agent owns the judgement, and this server makes no model calls of its
/// own.
/// </para>
/// <para>
/// Never served beside the note tools. They work, which is the problem: <c>note_index_next</c> in
/// an ordinary session is a several-hundred-iteration loop that rewrites files, and a declared tool
/// that can run but should not costs a burnt session rather than an error.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class NoteIndexTools(IndexRun run, NoteOptions options, IndexTarget target)
{
	[McpServerTool(
		Name = ToolNames.IndexNext,
		Title = "Claim the next note to enrich",
		ReadOnly = false,
		Destructive = false,
		Idempotent = false,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.IndexNext)]
	public NoteAssignment Next(
		[Description(ToolDescriptions.IndexScopeArgument)] string? scope = null,
		[Description("How long the claim is held, in minutes. Ten by default.")] int leaseMinutes = 10) =>
		Attribute(run.Next(Stores(), Scope(scope), leaseMinutes));

	[McpServerTool(
		Name = ToolNames.IndexWrite,
		Title = "Write a note's enrichment",
		ReadOnly = false,
		Destructive = false,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.IndexWrite)]
	public EnrichmentAccepted Write(
		[Description(ToolDescriptions.LeaseArgument)] string lease,
		[Description(ToolDescriptions.GistArgument)] string gist,
		[Description(ToolDescriptions.AsksArgument)] string[] asks,
		[Description(ToolDescriptions.TopicsArgument)] string[] topics,
		[Description(ToolDescriptions.EntitiesArgument)] string[]? entities = null,
		[Description(ToolDescriptions.AliasesArgument)] string[]? aliases = null,
		[Description(ToolDescriptions.LinksArgument)] string[]? links = null,
		[Description(ToolDescriptions.ConfidenceArgument)] string? confidence = null,
		[Description(ToolDescriptions.NewTopicsArgument)] string[]? newTopics = null,
		[Description(ToolDescriptions.IndexScopeArgument)] string? scope = null) =>
		Attribute(run.Write(
			Stores(),
			Scope(scope),
			lease,
			new Contracts.Enrichment
			{
				Gist = gist,
				Asks = asks,
				Topics = topics,
				Entities = entities ?? [],
				Aliases = aliases ?? [],
				Links = links ?? [],
				Confidence = IndexArguments.Confidence(confidence),
			},
			newTopics ?? []));

	[McpServerTool(
		Name = ToolNames.IndexSkip,
		Title = "Give a claim back",
		ReadOnly = false,
		Destructive = false,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.IndexSkip)]
	public SkipRecorded Skip(
		[Description(ToolDescriptions.LeaseArgument)] string lease,
		[Description(ToolDescriptions.DispositionArgument)] string disposition,
		[Description(ToolDescriptions.SkipReasonArgument)] string? reason = null,
		[Description(ToolDescriptions.IndexScopeArgument)] string? scope = null) =>
		Attribute(run.Skip(Stores(), Scope(scope), lease, IndexArguments.Disposition(disposition), reason));

	[McpServerTool(
		Name = ToolNames.IndexStatus,
		Title = "Indexing progress and vocabulary",
		ReadOnly = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.IndexStatus)]
	public IndexStatus Status(
		[Description(ToolDescriptions.DriftArgument)] bool includeDrift = false,
		[Description(ToolDescriptions.IndexScopeArgument)] string? scope = null) =>
		Attribute(run.Status(Stores(), Scope(scope), includeDrift));

	/// <summary>
	/// Destructive, because the widest selection commits an agent to redoing every note in the store
	/// -- which is hours and real money, and is not undone by asking again.
	/// </summary>
	[McpServerTool(
		Name = ToolNames.IndexRebuild,
		Title = "Put notes back in the queue",
		ReadOnly = false,
		Destructive = true,
		Idempotent = true,
		OpenWorld = false,
		UseStructuredContent = true)]
	[Description(ToolDescriptions.IndexRebuild)]
	public IndexStatus Rebuild(
		[Description(ToolDescriptions.SelectionArgument)] string selection,
		[Description(ToolDescriptions.IndexScopeArgument)] string? scope = null) =>
		Attribute(run.Rebuild(Stores(), Scope(scope), IndexArguments.Selection(selection)));

	private NoteStores Stores() => NoteStores.For(target.Directory, options);

	/// <summary>
	/// Which store this call is about. The run was started against one, and naming a different one
	/// is refused rather than honoured: a run that spanned both is how somebody meaning to enrich a
	/// dozen committed notes rewrites the whole of their vault.
	/// </summary>
	private NoteScope Scope(string? scope)
	{
		if (scope is not { Length: > 0 }) return target.Scope;

		var asked = ArgumentValues.WriteScope(scope);

		if (asked != target.Scope)
		{
			throw new ArgumentException(
				$"This run covers the {target.Scope.ToString().ToLowerInvariant()} store. Start another "
					+ "with --scope to index the other one.");
		}

		return asked;
	}

	/// <summary>Names the store that answered, at the one place every result in this mode passes.</summary>
	private T Attribute<T>(T result)
		where T : NoteResult =>
		result with
		{
			Repository = NoteStores.For(target.Directory, options).Repository.Key,
			Scope = target.Scope.ToString().ToLowerInvariant(),
		};
}

/// <summary>Which store an indexing run was started against, and where it was started.</summary>
public sealed record IndexTarget
{
	public required string Directory { get; init; }

	public required NoteScope Scope { get; init; }
}

/// <summary>
/// The index mode's string-valued enums, each refusing an unknown value.
/// <para>
/// The same reasoning as everywhere else, sharper here: a misspelt <c>selection</c> that defaulted
/// to the widest value would commit an agent to re-enriching a whole store, which is hours of work
/// and real money spent answering a question nobody asked.
/// </para>
/// </summary>
public static class IndexArguments
{
	/// <exception cref="ArgumentException">The disposition is not one of the three.</exception>
	public static SkipDisposition Disposition(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"release" => SkipDisposition.Release,
		"not-worth-indexing" => SkipDisposition.NotWorthIndexing,
		"unreadable" => SkipDisposition.Unreadable,
		_ => throw ArgumentValues.Unknown(
			"disposition", value, "release", "not-worth-indexing", "unreadable"),
	};

	/// <exception cref="ArgumentException">The selection is not one of the four.</exception>
	public static RebuildSelection Selection(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		"stale" => RebuildSelection.Stale,
		"failed" => RebuildSelection.Failed,
		"skipped" => RebuildSelection.Skipped,
		"all" => RebuildSelection.All,
		_ => throw ArgumentValues.Unknown("selection", value, "stale", "failed", "skipped", "all"),
	};

	/// <summary>Defaults, unlike the others: a missing judgement is a middling one, not a mistake.</summary>
	/// <exception cref="ArgumentException">The confidence is not one of the three.</exception>
	public static string Confidence(string? value) => value?.Trim().ToLowerInvariant() switch
	{
		null or "" or "medium" => "medium",
		"high" => "high",
		"low" => "low",
		_ => throw ArgumentValues.Unknown("confidence", value, "high", "medium", "low"),
	};
}
