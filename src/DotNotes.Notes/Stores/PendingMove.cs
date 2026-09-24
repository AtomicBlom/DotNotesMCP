namespace DotNotes.Notes.Stores;

/// <summary>
/// Machine notes for this repository that are filed under another key, and the move that would put
/// them where the key says.
/// <para>
/// Reported on every answer until it is made or dismissed, because the alternative is a store that
/// drops out of every answer with no word said -- the failure this server exists to remove, arriving
/// through a rename instead of a worktree.
/// </para>
/// </summary>
public sealed record PendingMove
{
	/// <summary>The store the key names, which the candidates would move into.</summary>
	public required string Resolved { get; init; }

	/// <summary>The stores the evidence attributes to this repository.</summary>
	public required IReadOnlyList<MoveCandidate> Candidates { get; init; }

	/// <summary>
	/// Whether machine notes are being written to the one candidate rather than to the resolved
	/// store, which does not exist yet.
	/// </summary>
	public required bool Redirected { get; init; }
}
