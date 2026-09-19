using DotNotes.Notes.Stores;

namespace DotNotes.UnitTests;

/// <summary>
/// That one writer holds a store at a time, and that a writer which dies holds nothing.
/// <para>
/// The second is the reason the lock is an open handle rather than a file whose contents mean
/// something. A lock record has to be expired by whoever finds it, which means deciding whether a
/// holder is dead or merely slow -- and deciding wrong either corrupts a store or wedges it. An
/// operating system handle is released by the operating system, so there is never a stale one.
/// </para>
/// </summary>
public sealed class StoreLockTests
{
	private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(200);

	[Test]
	public void One_holder_at_a_time()
	{
		using var fixture = GitFixture.Create();
		var store = fixture.Plain("vault");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		using var held = StoreLock.Take(store, Brief, local);

		Should.Throw<TimeoutException>(() => StoreLock.Take(store, Brief, local));
	}

	[Test]
	public void Releasing_lets_the_next_writer_in()
	{
		using var fixture = GitFixture.Create();
		var store = fixture.Plain("vault");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		StoreLock.Take(store, Brief, local).Dispose();

		using var second = StoreLock.Take(store, Brief, local);

		second.ShouldNotBeNull();
	}

	/// <summary>Two stores are two locks, so writing to one never waits on the other.</summary>
	[Test]
	public void Two_stores_do_not_contend()
	{
		using var fixture = GitFixture.Create();
		var local = GitFixture.Under(fixture.Root, "localappdata");

		using var first = StoreLock.Take(fixture.Plain("one"), Brief, local);
		using var second = StoreLock.Take(fixture.Plain("two"), Brief, local);

		second.Path.ShouldNotBe(first.Path);
	}

	/// <summary>
	/// Two spellings of one store must not take two locks, or both writers proceed and one write is
	/// lost. This is the same folding that keeps a differently-cased drive letter from splitting a
	/// repository's notes.
	/// </summary>
	[Test]
	public void Two_spellings_of_one_store_take_one_lock()
	{
		using var fixture = GitFixture.Create();
		var store = fixture.Plain("vault");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		using var held = StoreLock.Take(store, Brief, local);

		Should.Throw<TimeoutException>(
			() => StoreLock.Take(store + Path.DirectorySeparatorChar, Brief, local));
	}

	/// <summary>
	/// The lock never lives inside the store. A store may be a vault on a synced drive, where a lock
	/// file would be replicated to the other machine and be indistinguishable from a live one.
	/// </summary>
	[Test]
	public void The_lock_is_not_inside_the_store()
	{
		using var fixture = GitFixture.Create();
		var store = fixture.Plain("vault");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		using var held = StoreLock.Take(store, Brief, local);

		held.Path.ShouldNotStartWith(store);
		held.Path.ShouldStartWith(local);
	}

	/// <summary>
	/// A waiting writer takes the lock as soon as the holder lets go, rather than failing on a first
	/// try. Contention here is one whole-file write between two of the person's own sessions, and is
	/// over in milliseconds.
	/// </summary>
	[Test]
	public async Task A_waiting_writer_takes_it_when_the_holder_lets_go()
	{
		using var fixture = GitFixture.Create();
		var store = fixture.Plain("vault");
		var local = GitFixture.Under(fixture.Root, "localappdata");

		var held = StoreLock.Take(store, Brief, local);
		var waiting = Task.Run(() => StoreLock.Take(store, TimeSpan.FromSeconds(5), local));

		await Task.Delay(100);
		held.Dispose();

		using var taken = await waiting;

		taken.ShouldNotBeNull();
	}
}
