using System.Diagnostics;

using Newtonsoft.Json;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Information about package
/// </summary>
[ DebuggerDisplay( "{Id}" ) ]
public class PackageInfo
{
	/// <summary>
	///    Package ID
	/// </summary>
	[ JsonIgnore ]
	public string Id
	{
		get { return $"{Name} [{Version}]"; }
	}

	/// <summary>
	///    Name of the package
	/// </summary>
	public required string Name { get; set; }

	/// <summary>
	///    Version of the package
	/// </summary>
	public required Version Version { get; set; }

	/// <summary>
	///    Path to the assembly file of the package
	/// </summary>
	public required string AssemblyFilePath { get; set; }

	/// <summary>
	///    Authors of the package
	/// </summary>
	public string? Authors { get; set; }

	/// <summary>
	///    Copyright notice of the package
	/// </summary>
	public string? Copyright { get; set; }

	/// <summary>
	///    Package project homepage url
	/// </summary>
	public string? Homepage { get; set; }

	/// <summary>
	///    Data type of license
	/// </summary>
	public LicenseDataType LicenseDataType { get; set; }

	/// <summary>
	///    License data
	/// </summary>
	public string? License { get; set; }

	/// <summary>
	///    License notice
	/// </summary>
	public string? Notice { get; set; }

	/// <summary>
	///    Package IDs of related packages
	/// </summary>
	public List< string > RelatedPacakges { get; set; } = [ ];

	/// <summary>
	///    Directory containing *.nuspec file for this package
	/// </summary>
	[ JsonIgnore ]
	public string? NuspecDirPath { get; set; }

	/// <summary>
	///    Parent result object
	/// </summary>
	[ JsonIgnore ]
	public required PackagesResult Parent { get; set; }

	/// <summary>
	///    Alphabetical sorting for this packages
	/// </summary>
	public static int AlphaSort( PackageInfo l, PackageInfo r )
	{
		int compare = l.Name.CompareTo( r.Name, StringComparison.InvariantCultureIgnoreCase );
		if( compare == 0 )
		{
			compare = l.Version.CompareTo( r.Version );
		}

		return compare;
	}
}
