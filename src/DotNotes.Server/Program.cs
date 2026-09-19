using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;

namespace DotNotes.Server;

/// <summary>The console host. One binary, and the mode is chosen by argument.</summary>
internal static class Program
{
	private static async Task<int> Main(string[] args)
	{
		ServerOptions options;

		try
		{
			options = ServerOptions.Parse(args);
		}
		catch (ArgumentException exception)
		{
			await Console.Error.WriteLineAsync(exception.Message);
			await Console.Error.WriteLineAsync(ServerOptions.Usage);

			return 2;
		}

		if (options.Explain is { Length: > 0 } directory) return Explain(directory);

		return 0;
	}

	/// <summary>
	/// Prints what a directory resolves to, then exits.
	/// <para>
	/// Writing to stdout is correct here and forbidden everywhere else in this host: this is a
	/// command a person runs, not a transport carrying protocol frames.
	/// </para>
	/// </summary>
	private static int Explain(string directory)
	{
		RepositoryIdentity identity;

		try
		{
			identity = RepositoryIdentity.For(directory);
		}
		catch (DotNotesConfigurationException exception)
		{
			Console.Error.WriteLine(exception.Message);

			return 1;
		}

		Console.WriteLine($"origin      {identity.Origin}");
		Console.WriteLine($"kind        {identity.Kind}");
		Console.WriteLine($"worktree    {identity.Worktree ?? "-"}");
		Console.WriteLine($"root        {identity.Root ?? "-"}");
		Console.WriteLine($"common dir  {identity.CommonDirectory ?? "-"}");
		Console.WriteLine($"remote      {identity.Remote ?? "-"}");
		Console.WriteLine($"repository  {identity.Name}");
		Console.WriteLine($"key         {identity.Key}");
		Console.WriteLine($"named by    {identity.NamedBy}");
		Console.WriteLine($"config      {identity.Config?.Path ?? "- (repository scope is off here)"}");

		return 0;
	}
}
