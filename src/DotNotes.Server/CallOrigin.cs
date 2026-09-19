namespace DotNotes.Server;

/// <summary>
/// Which directory a call came from, for the length of that call.
/// <para>
/// The working directory is usually right: an editor launching an MCP server starts it in the
/// project it opened, which is what lets every tool answer with no setup call first. But it is the
/// client's choice rather than a guarantee, and a client that relays, or one started somewhere else
/// and pointed at a project later, has a better answer to give. A client that has one sends it in
/// <c>_meta</c>.
/// </para>
/// <para>
/// An ambient value set by a request filter rather than an argument on every tool, so no tool
/// declares it, no tool can forget it, and it costs nothing in the input schema of any of them.
/// </para>
/// </summary>
public static class CallOrigin
{
	/// <summary>The <c>_meta</c> key a client puts the calling directory under.</summary>
	public const string MetaKey = "dotnotes/originDirectory";

	private static readonly AsyncLocal<string?> Current = new();

	/// <summary>Where this call came from, or null to fall back to the working directory.</summary>
	public static string? Directory => Current.Value;

	/// <summary>Sets it for the length of a call.</summary>
	public static IDisposable Use(string? directory)
	{
		var previous = Current.Value;

		Current.Value = directory;

		return new Scope(previous);
	}

	private sealed class Scope(string? previous) : IDisposable
	{
		public void Dispose() => Current.Value = previous;
	}
}
