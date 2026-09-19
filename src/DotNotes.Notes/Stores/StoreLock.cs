using DotNotes.Notes.Configuration;
using DotNotes.Notes.Repositories;

namespace DotNotes.Notes.Stores;

/// <summary>
/// One writer at a time for one store, held as an open file handle.
/// <para>
/// A handle rather than a file whose contents mean something: the OS releases it however the holder
/// dies, so there is never a stale lock to clear and no process has to tidy up after being killed.
/// That is the whole reason to prefer it to a lock record somebody has to expire.
/// </para>
/// <para>
/// The file lives under the product folder in local application data, deliberately **not** inside
/// the store. A store may be an Obsidian vault on a synced drive, and a lock file there would be
/// replicated to the other machine minutes later, where it would be indistinguishable from a live
/// one. Keeping it local makes this an honest same-machine mutex -- which is the contention that
/// actually happens, two sessions or two indexing processes on one box -- and leaves cross-machine
/// safety to whole-file writes and content hashes, which do not depend on timing.
/// </para>
/// </summary>
public sealed class StoreLock : IDisposable
{
	private const int RetryMilliseconds = 25;

	private readonly FileStream _handle;

	private StoreLock(FileStream handle, string path)
	{
		_handle = handle;
		Path = path;
	}

	/// <summary>The lock file, named for the store it covers.</summary>
	public string Path { get; }

	/// <summary>
	/// Takes the lock, waiting up to <paramref name="timeout"/> for whoever holds it.
	/// <para>
	/// Waiting rather than failing, because the thing being waited for is one whole-file write and
	/// is over in milliseconds. A caller that gave up immediately would surface contention between
	/// two of the person's own sessions as a refusal they can do nothing about.
	/// </para>
	/// </summary>
	/// <exception cref="TimeoutException">Somebody else held it for the whole timeout.</exception>
	public static StoreLock Take(string storePath, TimeSpan timeout, string? localAppData = null)
	{
		var path = PathFor(storePath, localAppData);
		var deadline = DateTime.UtcNow + timeout;

		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

		while (true)
		{
			if (TryOpen(path) is { } handle) return new StoreLock(handle, path);

			if (DateTime.UtcNow >= deadline)
			{
				throw new TimeoutException(
					$"Another process has been writing to {storePath} for more than "
						+ $"{timeout.TotalSeconds:0.#} seconds. Its lock is {path}.");
			}

			Thread.Sleep(RetryMilliseconds);
		}
	}

	/// <summary>
	/// The lock file for a store: one per store, named by the folded path so two spellings of one
	/// store cannot take two locks and write over each other.
	/// </summary>
	public static string PathFor(string storePath, string? localAppData = null) =>
		System.IO.Path.Combine(
			MachineSettingsFile.DirectoryFor(localAppData),
			"locks",
			$"{CanonicalPath.Hash(storePath)}.lock");

	/// <summary>
	/// The handle, or null where somebody else holds it. Sharing nothing is what makes this a lock;
	/// <c>DeleteOnClose</c> keeps the directory from accumulating one file per store ever opened.
	/// </summary>
	private static FileStream? TryOpen(string path)
	{
		try
		{
			return new FileStream(
				path,
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.None,
				bufferSize: 1,
				FileOptions.DeleteOnClose);
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			// Another holder's DeleteOnClose can leave the entry briefly pending deletion, which
			// presents as a denial rather than a sharing violation. Both mean the same thing here:
			// not yet.
			return null;
		}
	}

	public void Dispose() => _handle.Dispose();
}
