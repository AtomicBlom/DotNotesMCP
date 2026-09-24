using DotNotes.Notes.Configuration;
using DotNotes.Notes.Stores;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

		if (options.Explain is { Length: > 0 } directory) return Explain(directory, options);

		if (options.Init is { Length: > 0 } opting) return Run(options, service => service.Init(opting));

		if (options.Adopt is { Length: > 0 } adopting) return Run(options, service => service.Adopt(adopting, options.Only));

		if (options.Dismiss is { Length: > 0 } dismissing) return Run(options, service => service.Dismiss(dismissing, options.Only));

		return await ServeAsync(options);
	}

	/// <summary>
	/// Runs one command against the stores, prints what it did, then exits. A command a person runs,
	/// which is why it writes to stdout: there is no protocol stream here to corrupt.
	/// </summary>
	private static int Run(ServerOptions options, Func<NoteService, IReadOnlyList<string>> command)
	{
		var notes = options.Notes();
		var service = new NoteService(notes, new DotNotes.Index.CrawlingNoteSearch(notes));

		try
		{
			var lines = command(service);

			foreach (var line in lines) Console.WriteLine(line);

			return 0;
		}
		catch (Exception exception) when (exception is McpRefusal or DotNotesConfigurationException
			or IOException or UnauthorizedAccessException or TimeoutException)
		{
			Console.Error.WriteLine(exception.Message);

			return 1;
		}
	}

	/// <summary>
	/// Serves the note tools over stdio.
	/// <para>
	/// Logging goes to stderr and nowhere else. stdout carries protocol frames, and a single stray
	/// write into it corrupts the stream in a way that presents as a protocol bug rather than as a
	/// print statement.
	/// </para>
	/// </summary>
	private static async Task<int> ServeAsync(ServerOptions options)
	{
		var builder = Host.CreateApplicationBuilder();

		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

		// One or the other, never both. The indexing tools work, which is exactly why a session that
		// is not here to index must not be offered them.
		if (options.Mode == ServerMode.Index)
		{
			builder.Services.AddDotNotesIndexing(options.Notes(), options.Scope!.Value)
				.WithStdioServerTransport();
		}
		else
		{
			builder.Services.AddDotNotes(options.Notes()).WithStdioServerTransport();
		}

		await builder.Build().RunAsync();

		return 0;
	}

	/// <summary>
	/// Prints what a directory resolves to, then exits.
	/// <para>
	/// Writing to stdout is correct here and forbidden in the served mode: this is a command a
	/// person runs, not a transport carrying protocol frames.
	/// </para>
	/// </summary>
	private static int Explain(string directory, ServerOptions options)
	{
		NoteStores stores;

		try
		{
			stores = NoteStores.For(directory, options.Notes());
		}
		catch (DotNotesConfigurationException exception)
		{
			Console.Error.WriteLine(exception.Message);

			return 1;
		}

		var identity = stores.Repository;

		Console.WriteLine($"origin       {identity.Origin}");
		Console.WriteLine($"kind         {identity.Kind}");
		Console.WriteLine($"worktree     {identity.Worktree ?? "-"}");
		Console.WriteLine($"root         {identity.Root ?? "-"}");
		Console.WriteLine($"common dir   {identity.CommonDirectory ?? "-"}");
		Console.WriteLine($"remote       {identity.Remote ?? "-"}");
		Console.WriteLine($"repository   {identity.Name}");
		Console.WriteLine($"key          {identity.Key}");
		Console.WriteLine($"named by     {identity.NamedBy}");
		Console.WriteLine($"roots        {(stores.Roots.Count > 0 ? string.Join(", ", stores.Roots) : "-")}");
		Console.WriteLine();
		Console.WriteLine($"machine name {stores.MachineName}");
		Console.WriteLine($"store root   {stores.MachineRoot}  ({stores.MachineRootSource})");
		Console.WriteLine($"  machine    {Describe(stores.Machine)}");
		Console.WriteLine($"  repository {Describe(stores.Repo)}");

		foreach (var also in stores.Also)
		{
			Console.WriteLine($"  also read  {also.Path}  ({also.Scope.ToString().ToLowerInvariant()})");
		}

		if (stores.Pending is { } pending)
		{
			Console.WriteLine();
			Console.WriteLine(pending.Redirected
				? $"pending      notes are written to {stores.Machine.Path} until --adopt moves them to {pending.Resolved}"
				: $"pending      --adopt merges the stores read above into {pending.Resolved}");
		}

		if (stores.Evidence.Unreadable is { } unreadable) Console.WriteLine($"evidence     {unreadable}");

		return 0;
	}

	/// <summary>A store as one line: where it is, or why it cannot be used.</summary>
	private static string Describe(NoteStore store) =>
		store.Unavailable is { } because ? because : store.Path;
}
