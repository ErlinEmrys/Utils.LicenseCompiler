using System.Reflection;
using System.Text;
using System.Xml.Serialization;

using Erlin.Lib.Common;
using Erlin.Lib.Common.Threading;
using Erlin.Lib.Common.Xml;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Class for resolving Nuget packages
/// </summary>
public static class PackageResolver
{
	private static readonly TaskWorker< PackageInfo > _packageWorker = new( "Package_Info_Reader", PackageResolver.WorkerProcessPackage, 32 );

	/// <summary>
	///    *.nuspec file serializer
	/// </summary>
	private static XmlSerializer NuspecSerializer { get; } = new( typeof( NuspecPackage ) );

	/// <summary>
	///    Resolves all packages of the project
	/// </summary>
	public static async Task< PackagesResult > ResolvePackages( ProgramArgs args, CancellationToken cancelToken = default )
	{
		PackagesResult result = new();

		// Read packages from ref file
		await PackageResolver.ReadRefFile( args, result, cancelToken );

		// Wait until all packages are processed
		await _packageWorker.WaitToFinish( cancelToken );

		// Sort pacakges
		result.Packages.Sort( PackageInfo.AlphaSort );
		result.MicrosoftPackages.Sort( PackageInfo.AlphaSort );

		// Proces Microsoft packages differently, becouse of the .NET Framework
		MicrosoftLicences.ProcessMicrosoftPackages( result );

		return result;
	}

	/// <summary>
	///    Read the solution for packages that are being referenced
	/// </summary>
	private static async Task ReadRefFile( ProgramArgs args, PackagesResult result, CancellationToken cancelToken = default )
	{
		if( !File.Exists( args.RefFilePath ) )
		{
			throw new FileNotFoundException( "References file not found: " + args.RefFilePath );
		}

		string[] lines = await File.ReadAllLinesAsync( args.RefFilePath, Encoding.UTF8, cancelToken );
		foreach( string fLine in lines )
		{
			if( fLine.IsNotEmpty() )
			{
				string[] parts = fLine.Trim( '\'' ).Split( '|' );
				if( parts.Length < 3 )
				{
					Log.Wrn( "Invalid line in references file: {Line}", fLine );
					continue;
				}

				AssemblyName assemblyName;
				try
				{
					assemblyName = new AssemblyName( parts[ 0 ] );
				}
				catch( Exception )
				{
					Log.Wrn( "Invalid assembly name in references file: {Line}", fLine );
					continue;
				}

				string nugetPackageId = parts[ 1 ];
				string assemblyFilePath = parts[ 2 ];

				PackageInfo package = new()
				{
					Name = assemblyName.Name,
					Version = assemblyName.Version,
					AssemblyFilePath = assemblyFilePath,
					Parent = result
				};

				if( MicrosoftLicences.IsMicrosoftPackage( nugetPackageId ) )
				{
					result.AddMicrosoftPackage( package );
				}
				else
				{
					result.AddPackage( package );
					_packageWorker.Enqueue( package );
				}
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
	///    Deserialize *.nuspec file for selected package
	/// </summary>
	private static void ReadNuspecFile( PackageInfo package )
	{
		Log.Dbg( "Reading .nuspec package {PackageID}", package.Id );
		package.NuspecDirPath = Path.Combine( FileSystemHelper.GetDirectoryPath( package.AssemblyFilePath ), @".\..\.." );

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
}
