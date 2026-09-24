using DotNotes.Contracts;

using DotNotes.Notes.Configuration;

namespace DotNotes.Server;

/// <summary>What the host was asked to do, parsed from the command line and nowhere else.</summary>
public sealed record ServerOptions
{
	/// <summary>
	/// An explicit machine store, ahead of the environment and the settings file. The reason to pass
	/// it is a store somewhere other than the one this machine has configured.
	/// </summary>
	public string? Store { get; init; }

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

	/// <summary>
	/// A repository whose pending move to make, then exit. The explicit step a notice names: a store
	/// is never moved except by somebody running this.
	/// </summary>
	public string? Adopt { get; init; }

	/// <summary>A repository whose pending move to dismiss, then exit: its candidates are another repository's.</summary>
	public string? Dismiss { get; init; }

	/// <summary>A checkout to opt in to committed notes, then exit: the command a repository-scope refusal names.</summary>
	public string? Init { get; init; }

	/// <summary>One candidate's folder name, narrowing <see cref="Adopt"/> or <see cref="Dismiss"/> to it.</summary>
	public string? Only { get; init; }

	/// <summary>
	/// Which surface to serve. The two are never served together: the indexing tools work, and that
	/// is the problem -- a several-hundred-iteration loop that rewrites files, in front of a session
	/// doing something else, costs a burnt session rather than an error.
	/// </summary>
	public ServerMode Mode { get; init; } = ServerMode.Serve;

	/// <summary>Which store an indexing run covers. Required, because a run that spans both rewrites both.</summary>
	public NoteScope? Scope { get; init; }

	/// <summary>The usage line, printed to stderr beside whatever was wrong with the arguments.</summary>
	public const string Usage =
		"usage: DotNotes.Server [--mode serve|index] [--scope machine|repository] [--root <dir>] "
			+ "[--store <path>] [--explain <dir>] [--init <dir>] [--adopt <dir> | --dismiss <dir>] [--only <folder>]";

	/// <exception cref="ArgumentException">An argument is unrecognised, or its value is missing.</exception>
	public static ServerOptions Parse(string[] args)
	{
		var root = Environment.CurrentDirectory;
		var mode = ServerMode.Serve;
		NoteScope? scope = null;
		string? store = null;
		string? explain = null;
		string? adopt = null;
		string? dismiss = null;
		string? only = null;
		string? init = null;

		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--mode":
					if (i + 1 >= args.Length) throw new ArgumentException("--mode requires serve or index.");
					mode = args[++i].Trim().ToLowerInvariant() switch
					{
						"serve" => ServerMode.Serve,
						"index" => ServerMode.Index,
						var given => throw new ArgumentException($"Unknown mode '{given}'. Use serve, index."),
					};

					break;

				case "--scope":
					if (i + 1 >= args.Length) throw new ArgumentException("--scope requires machine or repository.");
					scope = ArgumentValues.WriteScope(args[++i]);
					break;

				case "--root":
					if (i + 1 >= args.Length) throw new ArgumentException("--root requires a directory.");
					root = args[++i];
					break;

				case "--store":
					if (i + 1 >= args.Length) throw new ArgumentException("--store requires a path.");
					store = args[++i];
					break;

				case "--explain":
					if (i + 1 >= args.Length) throw new ArgumentException("--explain requires a directory.");
					explain = args[++i];
					break;

				case "--adopt":
					if (i + 1 >= args.Length) throw new ArgumentException("--adopt requires a directory.");
					adopt = args[++i];
					break;

				case "--dismiss":
					if (i + 1 >= args.Length) throw new ArgumentException("--dismiss requires a directory.");
					dismiss = args[++i];
					break;

				case "--init":
					if (i + 1 >= args.Length) throw new ArgumentException("--init requires a directory.");
					init = args[++i];
					break;

				case "--only":
					if (i + 1 >= args.Length) throw new ArgumentException("--only requires a store's folder name.");
					only = args[++i];
					break;

				default:
					throw new ArgumentException($"Unrecognised argument '{args[i]}'.");
			}
		}

		var options = new ServerOptions
		{
			Root = root,
			Mode = mode,
			Scope = scope,
			Store = store,
			Explain = explain,
			Adopt = adopt,
			Dismiss = dismiss,
			Only = only,
			Init = init,
		};

		options.Validate();

		return options;
	}

	/// <summary>
	/// Refuses an indexing run that has not said which store it covers.
	/// <para>
	/// Indexing rewrites frontmatter in every note it touches, and the two stores are a repository
	/// working tree and, quite possibly, a whole Obsidian vault. A run that covered both because
	/// nobody said otherwise is how somebody meaning to enrich a dozen committed notes rewrites nine
	/// hundred personal ones.
	/// </para>
	/// </summary>
	/// <exception cref="ArgumentException">An indexing run names no store.</exception>
	private void Validate()
	{
		if (Adopt is not null && Dismiss is not null)
		{
			throw new ArgumentException("--adopt and --dismiss are opposite answers to one question. Give one.");
		}

		if (Only is not null && Adopt is null && Dismiss is null)
		{
			throw new ArgumentException("--only narrows --adopt or --dismiss, and neither was given.");
		}

		if (Mode != ServerMode.Index || Scope is not null) return;

		throw new ArgumentException(
			"Indexing writes into every note it enriches, so it will not span both stores. Name one: "
				+ "--scope machine, or --scope repository.");
	}

	/// <summary>
	/// What the arguments say about the stores. The one place a command line becomes the options the
	/// rest of the server reads, so nothing further in has to know a command line exists.
	/// </summary>
	public NoteOptions Notes() => new() { DefaultRoot = Root, MachineStore = Store };
}
