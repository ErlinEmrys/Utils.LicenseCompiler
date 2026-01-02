namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Result of the license generator
/// </summary>
public class GeneratorResult
{
	/// <summary>
	///    Path to packages cache
	/// </summary>
	public required string PackagesPath { get; set; }

	/// <summary>
	///    List of packages that project depends upon
	/// </summary>
	public List< PackageInfo > Packages { get; } = [ ];

	/// <summary>
	///    List of Microsoft packages
	/// </summary>
	public List< PackageInfo > MicrosoftPackages { get; } = [ ];

	/// <summary>
	///    Adds packages to this result object
	/// </summary>
	public void AddPackages( List< PackageInfo > list )
	{
		foreach( PackageInfo fPackage in list )
		{
			AddPackage( fPackage );
		}
	}

	/// <summary>
	///    Adds package to this result object
	/// </summary>
	public void AddPackage( PackageInfo package )
	{
		package.Parent = this;
		Packages.Add( package );
	}

	public void AddMicrosoftPackage( PackageInfo package )
	{
		Packages.Remove( package );
		MicrosoftPackages.Add( package );
	}
}
