namespace DotNotes.Contracts;

/// <summary>
/// Which layer supplied the machine store's path. Reported rather than inferred: four layers can
/// each name a different directory, and "my notes went somewhere else" is otherwise answered by
/// guessing which one won.
/// </summary>
public enum MachineStoreSource
{
	/// <summary>A <c>--store</c> argument.</summary>
	Argument,

	/// <summary>The <c>DOTNOTES_STORE</c> environment variable.</summary>
	Environment,

	/// <summary>The <c>machineStore</c> entry of this machine's settings file.</summary>
	Settings,

	/// <summary>
	/// Nothing said otherwise, so the store is beside the settings that could have moved it. The
	/// only source that is created on demand, because it is the one nobody chose.
	/// </summary>
	Default,
}
