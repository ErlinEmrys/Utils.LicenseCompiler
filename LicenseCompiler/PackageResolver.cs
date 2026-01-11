using System.Globalization;
using System.Reflection;
using System.Xml.Serialization;

using Erlin.Lib.Common;
using Erlin.Lib.Common.Exceptions;
using Erlin.Lib.Common.Threading;
using Erlin.Lib.Common.Xml;

using Microsoft.Build.Construction;

using SimpleExec;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Class for resolving Nuget packages
/// </summary>
public static class PackageResolver
{
	private const string CMD = "dotnet";
	private const string CMD_ARGS_PATH = "nuget locals -l global-packages --force-english-output";

	private static readonly HashSet< string > _idSet = [ ];
	private static readonly TaskWorker< PackageInfo > _packageWorker = new( "Package_Info_Reader", PackageResolver.WorkerProcessPackage, 32 );

	/// <summary>
	///    *.nuspec file serializer
	/// </summary>
	private static XmlSerializer NuspecSerializer { get; } = new( typeof( NuspecPackage ) );

	/// <summary>
	///    Resolves all packages of the project
	/// </summary>
	public static async Task< CompilerResult > ResolvePackages( ProgramArgs args )
	{
		// Retrieve physical path to packages
		string packagesPath = await PackageResolver.ResolvePackagesPath();
		CompilerResult result = new() { PackagesPath = packagesPath };

		// Read packages from soolution
		PackageResolver.ReadSolution( args, result );

		// Wait until all packages are processed
		await _packageWorker.WaitToFinish();

		// Sort pacakges
		result.Packages.Sort( PackageInfo.AlphaSort );
		result.MicrosoftPackages.Sort( PackageInfo.AlphaSort );

		// Remove duplicates
		PackageResolver.FilterOutDuplicates( result );

		// Proces Microsoft packages differently, becouse of the .NET Framework
		MicrosoftLicences.ProcessMicrosoftPackages( result );

		return result;
	}

	/// <summary>
	///    Read the solution for packages that are being referenced
	/// </summary>
	private static void ReadSolution( ProgramArgs args, CompilerResult result )
	{
		const string CS_PROJECT_EXTENSION = ".csproj";
		const string ITEM_NAME_PACKAGE_REFERENCE = "PackageReference";
		const string METADATA_NAME_PRIVATE_ASSETS = "PrivateAssets";
		const string METADATA_NAME_VERSION = "Version";
		const string METADATA_VALUE_ALL = "all";

		if( !File.Exists( args.SolutionFilePath ) )
		{
			throw new FileNotFoundException( "Solution file not found", args.SolutionFilePath );
		}

		SolutionFile solutionFile = SolutionFile.Parse( args.SolutionFilePath );
		foreach( ProjectInSolution fProject in solutionFile.ProjectsInOrder )
		{
			if( Path.GetExtension( fProject.AbsolutePath ).EqualsTo( CS_PROJECT_EXTENSION ) )
			{
				Log.Dbg( "Reading cs_project: {ProjectPath}", fProject.AbsolutePath );

				ProjectRootElement project = ProjectRootElement.Open( fProject.AbsolutePath );
				foreach( ProjectItemElement fProjectItem in project.Items )
				{
					if( fProjectItem.ItemType.EqualsTo( ITEM_NAME_PACKAGE_REFERENCE ) &&
						!fProjectItem.Metadata.Any( m =>
							m.Name.EqualsTo( METADATA_NAME_PRIVATE_ASSETS ) &&
							m.Value.EqualsTo( METADATA_VALUE_ALL ) ) )
					{
						ProjectMetadataElement? version = fProjectItem.Metadata.FirstOrDefault( m => m.Name.EqualsTo( METADATA_NAME_VERSION ) );
						if( version is not null )
						{
							PackageResolver.RegisterPackage( result, fProjectItem.Include, version.Value );
						}
					}
				}
			}
		}
	}

	/// <summary>
	///    Register package for processing
	/// </summary>
	private static void RegisterPackage( CompilerResult result, string name, string version )
	{
		PackageInfo package = new()
		{
			Name = name,
			Version = new Version( version ),
			Parent = result
		};

		lock( _idSet )
		{
			if( _idSet.Add( package.Id ) )
			{
				Log.Inf( "Package reference found: {PackageID}", package.Id );
				_packageWorker.Enqueue( package );
				result.AddPackage( package );
			}
		}
	}

	/// <summary>
	///    Process package from the worker
	/// </summary>
	private static Task WorkerProcessPackage( PackageInfo package, CancellationToken token = default )
	{
		PackageResolver.ReadNuspecFile( package );
		return Task.CompletedTask;
	}

	/// <summary>
	///    Utility for resolving physical path to local packages cache
	/// </summary>
	private static async Task< string > ResolvePackagesPath()
	{
		string packagesPath = await PackageResolver.ExecuteCommand( CMD, CMD_ARGS_PATH );

		const string PATH_PREFIX = "global-packages:";
		if( packagesPath.IsEmpty() || !packagesPath.StartsWith( PATH_PREFIX, true, CultureInfo.InvariantCulture ) )
		{
			throw new UnexpectedResultException( $"Unexpected output: {packagesPath} for command: {CMD} {CMD_ARGS_PATH}" );
		}

		packagesPath = packagesPath[ PATH_PREFIX.Length.. ].Trim();
		if( !Directory.Exists( packagesPath ) )
		{
			throw new UnexpectedResultException( $"Packages directory {packagesPath} not exist" );
		}

		Log.Dbg( "Packages path resolved: {Path}", packagesPath );

		return packagesPath;
	}

	/// <summary>
	///    Deserialize *.nuspec file for selected package
	/// </summary>
	private static void ReadNuspecFile( PackageInfo package )
	{
		if( MicrosoftLicences.IsMicrosoftPackage( package.Name ) )
		{
			PackageResolver.ProcessMicrosoftPackage( package );
			return;
		}

		Log.Dbg( "Reading .nuspec package {PackageID}", package.Id );
		package.NuspecDirPath = Path.Combine( package.Parent.PackagesPath, Utils.ToLower( package.Name ), Utils.ToLower( package.Version.ToString() ) );

		string nuspecFilePath = Path.Combine( package.NuspecDirPath, Utils.ToLower( package.Name ) + ".nuspec" );

		if( File.Exists( nuspecFilePath ) )
		{
			using StreamReader reader = new( nuspecFilePath );
			if( PackageResolver.NuspecSerializer.Deserialize( new IgnoreNamespaceXmlReader( reader ) ) is NuspecPackage nuspec )
			{
				PackageResolver.FillInfo( nuspec, package );
			}
		}
		else
		{
			Log.Wrn( "Package {PackageID} not found in {Path}", package.Id, package.NuspecDirPath );
		}
	}

	/// <summary>
	///    Processing of Microsoft package
	/// </summary>
	private static void ProcessMicrosoftPackage( PackageInfo package )
	{
		Log.Dbg( "Microsoft package found {PackageID}", package.Id );
		package.Parent.AddMicrosoftPackage( package );

		Assembly? a = null;
		try
		{
			a = Assembly.Load( package.Name + ", Version=" + package.Version + ", Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a" );
		}
		catch( Exception ex ) when( ex is FileNotFoundException or FileLoadException )
		{
			try
			{
				a = Assembly.Load( package.Name );
			}
			catch( Exception )
			{
				Log.Wrn( "Unable to open assembly: {Assembly}", package.Id );
			}
		}

		if( a is not null )
		{
			AssemblyName[] names = a.GetReferencedAssemblies();
			foreach( AssemblyName fAssemblyName in names )
			{
				if( fAssemblyName.Name.IsNotEmpty() && fAssemblyName.Version is not null )
				{
					PackageResolver.RegisterPackage( package.Parent, fAssemblyName.Name, fAssemblyName.Version.ToString() );
				}
			}
		}
	}

	/// <summary>
	///    Reads info from *.nuspec package
	/// </summary>
	private static void FillInfo( NuspecPackage nuget, PackageInfo info )
	{
		if( nuget.Metadata != null )
		{
			PackageResolver.FillInfo( nuget.Metadata, info );
		}
	}

	/// <summary>
	///    Reads info from *.nuspec package metadata
	/// </summary>
	private static void FillInfo( NuspecMetadata nuget, PackageInfo info )
	{
		if( nuget.DependencyGroups?.Length > 0 )
		{
			foreach( NuspecDependencyGroup fGroup in nuget.DependencyGroups )
			{
				if( fGroup.Dependencies is not null )
				{
					foreach( NuspecDependency fDep in fGroup.Dependencies )
					{
						if( fDep.Id.IsNotEmpty() && fDep.Version.IsNotEmpty() )
						{
							PackageResolver.RegisterPackage( info.Parent, fDep.Id, fDep.Version );
						}
					}
				}
			}
		}

		info.Authors = nuget.Authors;
		info.Copyright = nuget.Copyright;
		info.Homepage = nuget.ProjectUrl;
		if( info.Homepage.IsEmpty() )
		{
			info.Homepage = nuget.Repository?.Url;
		}

		info.Notice = FileResolver.GetNoticeFile( info.NuspecDirPath );
		PackageResolver.FillLicense( nuget, info );
	}

	/// <summary>
	///    Reads license data from *.nuspec package metadata
	/// </summary>
	private static void FillLicense( NuspecMetadata nuget, PackageInfo info )
	{
		if( nuget.License != null )
		{
			string? filePath = null;
			if( nuget.License.Type == "file" )
			{
				filePath = nuget.License.Text;
			}

			// 1. LICENSE file
			info.License = FileResolver.GetLicenseFile( info.NuspecDirPath, filePath );

			if( info.License.IsNotEmpty() )
			{
				info.LicenseDataType = LicenseDataType.Text;
			}
			else if( info.License.IsEmpty() && filePath.IsNotEmpty() )
			{
				info.LicenseDataType = LicenseDataType.Error;
				info.License = $"License specified as packaged file: {filePath}, but no such file found in package!";
			}
			else if( nuget.License.Type == "expression" )
			{
				// 2. Expression
				info.LicenseDataType = LicenseDataType.Expression;
				info.License = nuget.License.Text;
				if( info.License.IsEmpty() )
				{
					info.LicenseDataType = LicenseDataType.Error;
					info.License = "License specified as expression, but no expression provided by the package!";
				}
			}
		}

		if( ( info.LicenseDataType == LicenseDataType.EnumNullError ) && nuget.LicenseUrl.IsNotEmpty() )
		{
			// 3. URL
			info.LicenseDataType = LicenseDataType.Url;
			info.License = nuget.LicenseUrl;

			if( info.License.IsEmpty() )
			{
				info.LicenseDataType = LicenseDataType.Error;
				info.License = "License specified as URL, but no URL provided by the package!";
			}
			else if( !Utils.CheckUrl( info.License ) )
			{
				info.LicenseDataType = LicenseDataType.Error;
				info.License = "License specified as URL, but the URL is not valid: " + info.License;
			}
		}

		if( info.LicenseDataType == LicenseDataType.EnumNullError )
		{
			info.LicenseDataType = LicenseDataType.Error;
			info.License = $"Package does not contain a supported license: {nuget.License?.Type}/{nuget.License?.Text}";
			Log.Wrn( "Package {PackageId} does not contain a supported license", info.Id );
		}
	}

	/// <summary>
	///    Executes shell command
	/// </summary>
	/// <param name="cmd">Command to execute</param>
	/// <param name="cmdArgs">Command arguments</param>
	/// <returns>Command result</returns>
	private static async Task< string > ExecuteCommand( string cmd, string cmdArgs )
	{
		int cmdErrorCode = 0;
		( string cmdOutput, string cmdError ) = await Command.ReadAsync( cmd, cmdArgs,
			handleExitCode: code =>
			{
				cmdErrorCode = code;
				return true;
			} );

		if( cmdErrorCode != 0 )
		{
			throw new UnexpectedResultException( $"CMD: {cmd} {cmdArgs}{Environment.NewLine}ERROR: {cmdError}{Environment.NewLine}OUTPUT: {cmdOutput}" );
		}

		return cmdOutput;
	}

	/// <summary>
	///    Remove duplicates from result object
	/// </summary>
	private static void FilterOutDuplicates( CompilerResult result )
	{
		PackageResolver.FilterOutDuplicates( result.Packages );
		PackageResolver.FilterOutDuplicates( result.MicrosoftPackages );
	}

	/// <summary>
	///    Remove duplicates from list of packages
	/// </summary>
	private static void FilterOutDuplicates( List< PackageInfo > list )
	{
		for( int i = 0; i < list.Count; )
		{
			PackageInfo first = list[ i ];
			bool firstStays = true;
			for( int j = i + 1; j < list.Count; )
			{
				PackageInfo second = list[ j ];
				if( first.Name.EqualsTo( second.Name ) )
				{
					if( first.Version > second.Version )
					{
						list.RemoveAt( j );
					}
					else
					{
						list.RemoveAt( i );
						firstStays = false;
						break;
					}
				}
				else
				{
					j++;
				}
			}

			if( firstStays )
			{
				i++;
			}
		}
	}
}
