namespace DotNotes.Server;

/// <summary>What the host was asked to do, parsed from the command line and nowhere else.</summary>
public sealed record ServerOptions
{
	/// <summary>
	/// Where the repository is looked for when a call carries no path of its own. The process
	/// working directory by default, which for an MCP server an editor launched is the project it
	/// opened -- and is what lets every tool work with no setup call first.
	/// </summary>
	public string Root { get; init; } = Environment.CurrentDirectory;

	/// <summary>
	/// A directory to report on, then exit. No server starts, so this answers what the server would
	/// answer without anything having to be registered first.
	/// </summary>
	public string? Explain { get; init; }

	/// <summary>The usage line, printed to stderr beside whatever was wrong with the arguments.</summary>
	public const string Usage = "usage: DotNotes.Server [--root <dir>] [--explain <dir>]";

	/// <exception cref="ArgumentException">An argument is unrecognised, or its value is missing.</exception>
	public static ServerOptions Parse(string[] args)
	{
		var root = Environment.CurrentDirectory;
		string? explain = null;

		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--root":
					if (i + 1 >= args.Length) throw new ArgumentException("--root requires a directory.");
					root = args[++i];
					break;

				case "--explain":
					if (i + 1 >= args.Length) throw new ArgumentException("--explain requires a directory.");
					explain = args[++i];
					break;

				default:
					throw new ArgumentException($"Unrecognised argument '{args[i]}'.");
			}
		}

		return new ServerOptions { Root = root, Explain = explain };
	}
}
