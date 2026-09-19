namespace DotNotes.UnitTests;

/// <summary>
/// One environment variable, set for as long as a test needs it and restored afterwards.
/// <para>
/// Restoring means putting back what was there, including the difference between absent and empty:
/// a variable set to the empty string is a value, and a test that left one behind would change what
/// the next test resolves without appearing in it.
/// </para>
/// </summary>
public sealed class EnvironmentVariable : IDisposable
{
	private readonly string _name;
	private readonly string? _previous;

	private EnvironmentVariable(string name, string? value)
	{
		_name = name;
		_previous = Environment.GetEnvironmentVariable(name);

		Environment.SetEnvironmentVariable(name, value);
	}

	public static EnvironmentVariable Set(string name, string? value) => new(name, value);

	public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
}
