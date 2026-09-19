namespace DotNotes.Contracts;

/// <summary>
/// Every tool name, in one place so a caller and its callee cannot spell one differently.
/// <para>
/// Two sets, and they are never served together. The note tools are what an ordinary session sees.
/// The index tools are the enrichment loop, and a session doing anything else must not be offered
/// them: they work, which is the problem -- a declared tool that can run but should not is a burnt
/// session rather than an error.
/// </para>
/// </summary>
public static class ToolNames
{
	public const string Search = "note_search";
	public const string Read = "note_read";
	public const string Context = "note_context";
	public const string Write = "note_write";
	public const string Delete = "note_delete";
	public const string Move = "note_move";
	public const string Check = "note_check";

	/// <summary>The indexing loop. Served only by --mode index, and never beside the note tools.</summary>
	public const string IndexNext = "note_index_next";
	public const string IndexWrite = "note_index_write";
	public const string IndexSkip = "note_index_skip";
	public const string IndexStatus = "note_index_status";
	public const string IndexRebuild = "note_index_rebuild";
}
