using System.Globalization;
using System.Reflection;
using System.Xml.Serialization;

using Erlin.Lib.Common;
using Erlin.Lib.Common.Exceptions;
using Erlin.Lib.Common.Threading;
using Erlin.Lib.Common.Xml;

using Microsoft.Build.Construction;

using Newtonsoft.Json.Linq;

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
	private static readonly TaskWorker< PackageInfo > _worker = new( "Package_Info_Reader", PackageResolver.ProcessPackage, 32 );

	/// <summary>
	///    *.nuspec file serializer
	/// </summary>
	public static XmlSerializer NuspecSerializer { get; } = new( typeof( NuspecPackage ) );

	/// <summary>
	///    Resolves all packages of the project
	/// </summary>
	public static async Task< GeneratorResult > ResolvePackages( ProgramArgs args )
	{
		// Retrieve physical path to packages
		string packagesPath = await PackageResolver.ResolvePackagesPath();
		GeneratorResult result = new() { PackagesPath = packagesPath };

		PackageResolver.ReadSolution( args, result );

		await _worker.WaitToFinish();

		result.Packages.Sort( PackageInfo.AlphaSort );
		result.MicrosoftPackages.Sort( PackageInfo.AlphaSort );

		PackageResolver.FilterOutDuplicates( result );

		MicrosoftLicences.ProcessMicrosoftPackages( result );

		return result;
	}

	private static void FilterOutDuplicates( GeneratorResult result )
	{
		PackageResolver.FilterOutDuplicates( result.Packages );
		PackageResolver.FilterOutDuplicates( result.MicrosoftPackages );
	}

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

	private static void ReadSolution( ProgramArgs args, GeneratorResult result )
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

	private static void RegisterPackage( GeneratorResult result, string name, string version )
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
				_worker.Enqueue( package );
				result.AddPackage( package );
			}
		}
	}

	private static Task ProcessPackage( PackageInfo package, CancellationToken token = default )
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

	private static void ProcessMicrosoftPackage( PackageInfo package )
	{
		Log.Dbg( "Microsoft package found {PackageID}", package.Id );
		package.Parent.AddMicrosoftPackage( package );

		Assembly? a = null;
		try
		{
			a = Assembly.Load( package.Name ); // + ", Version=" + package.Version + ", Culture=neutral, PublicKeyToken=" );
		}
		catch( Exception ex ) when( ex is FileNotFoundException or FileLoadException )
		{
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
	public static void FillInfo( NuspecMetadata nuget, PackageInfo info )
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
	///    Reads basic packages info from JSON list
	/// </summary>
	private static void ConvertPackagesJson( GeneratorResult result, JObject json )
	{
		if( json[ "projects" ] is not JArray projects )
		{
			throw new UnexpectedResultException( "Packages JSON: Missing 'projects' array" );
		}

		Dictionary< string, PackageInfo > tempDic = new();
		foreach( JToken fProject in projects )
		{
			if( fProject[ "frameworks" ] is not JArray frameworks )
			{
				throw new UnexpectedResultException( "Packages JSON: Missing 'frameworks' array" );
			}

			foreach( JToken fFramework in frameworks )
			{
				JArray? topLevelPackages = fFramework.SelectToken( "topLevelPackages" ) as JArray;

				PackageResolver.PackageArrToInfo( result, tempDic, topLevelPackages );
			}
		}

		List< PackageInfo > list = tempDic.Values.ToList();
		list.Sort( ( l, r ) =>
		{
			int comparison = string.Compare( l.Name, r.Name, StringComparison.OrdinalIgnoreCase );
			if( comparison == 0 )
			{
				comparison = l.Version.CompareTo( r.Version );
			}

			return comparison;
		} );

		result.AddPackages( list );
	}

	/// <summary>
	///    Reads basic packages info from JSON list
	/// </summary>
	private static void PackageArrToInfo( GeneratorResult genResult, Dictionary< string, PackageInfo > result, JArray? array )
	{
		if( array != null )
		{
			foreach( JToken fPackage in array )
			{
				string? id = fPackage[ "id" ]?.Value< string >();
				string? version = fPackage[ "resolvedVersion" ]?.Value< string >();

				if( id.IsEmpty() || version.IsEmpty() )
				{
					throw new UnexpectedResultException( $"Packages JSON: Package missing 'id' or 'version': {fPackage}" );
				}

				PackageInfo info = new()
				{
					Name = id,
					Version = new Version( version ),
					Parent = genResult
				};

				result.TryAdd( info.Id, info );
			}
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
}
