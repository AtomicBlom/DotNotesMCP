namespace DotNotes.Notes.Configuration;

/// <summary>Where the stores are and what a note may be, for a host that has not said otherwise.</summary>
public sealed class NoteOptions
{
	/// <summary>
	/// Where a repository is looked for when a call carries no path. The process working directory
	/// by default, which for an MCP server an editor launched is the project it opened, and is what
	/// lets every tool answer with no setup call first.
	/// </summary>
	public string DefaultRoot { get; set; } = System.Environment.CurrentDirectory;

	/// <summary>
	/// An explicit machine store, ahead of the environment and the settings file. Set from
	/// <c>--store</c>, and by a test that wants a store it can delete.
	/// </summary>
	public string? MachineStore { get; set; }

	/// <summary>
	/// Where local application data is. Only a test sets this, so that the settings file, the locks
	/// and the default store are all somewhere disposable rather than in the real profile.
	/// </summary>
	public string? LocalAppData { get; set; }

	/// <summary>
	/// How an environment variable is read. Only a test replaces it.
	/// <para>
	/// A seam rather than a real variable, because the process environment is shared by everything
	/// running in it. A test that set <c>DOTNOTES_STORE</c> to exercise precedence redirected every
	/// other test running beside it into the same directory, where they collided over each other's
	/// files -- a failure that moved around the suite from run to run and named the wrong tests.
	/// </para>
	/// </summary>
	public Func<string, string?> Environment { get; set; } = System.Environment.GetEnvironmentVariable;

	/// <summary>
	/// The longest body a note may carry. A note is read whole or not at all, and one real memory in
	/// the store this replaces is 41 KB -- which is not a note, it is a document nobody finishes. A
	/// write over this is refused and names the headings to split at.
	/// </summary>
	public int MaxBodyCharacters { get; set; } = 6000;

	/// <summary>How long a write waits for another process holding the same store.</summary>
	public TimeSpan StoreLockTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
