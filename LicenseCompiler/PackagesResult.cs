namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Result of the packages resolver
/// </summary>
public class PackagesResult
{
	/// <summary>
	///    List of packages that project depends upon
	/// </summary>
	public List< PackageInfo > Packages { get; } = [ ];

	/// <summary>
	///    List of Microsoft packages that project depends upon
	/// </summary>
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
