using Newtonsoft.Json;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Result of the packages resolver
/// </summary>
public class PackagesResult
{
	/// <summary>
	///    License for the current project
	/// </summary>
	public string? ProjectLicense { get; init; }

	/// <summary>
	///    List of packages that project depends upon
	/// </summary>
	public List< PackageInfo > Packages { get; } = [ ];

	/// <summary>
	///    Program arguments
	/// </summary>
	[ JsonIgnore ]
	public required ProgramArgs Args { get; init; }

	/// <summary>
	///    List of Microsoft packages that project depends upon
	/// </summary>
	[ JsonIgnore ]
	public List< PackageInfo > MicrosoftPackages { get; } = [ ];

	/// <summary>
	///    Adds package to this result object
	/// </summary>
	public void AddPackage( PackageInfo package )
	{
		package.Parent = this;
		Packages.Add( package );
	}

	/// <summary>
	///    Add packages from Microsoft to this result object
	/// </summary>
	public void AddMicrosoftPackage( PackageInfo package )
	{
		package.Parent = this;
		MicrosoftPackages.Add( package );
	}
}
