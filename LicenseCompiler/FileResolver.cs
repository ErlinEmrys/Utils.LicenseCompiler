namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Resolver for packaged files
/// </summary>
public static class FileResolver
{
	/// <summary>
	///    All common license file names
	/// </summary>
	private static string[] LicenseFileNames { get; } = [ "LICENSE", "LICENCE" ];

	/// <summary>
	///    All common notice file names
	/// </summary>
	private static string[] NoticeFileNames { get; } = [ "NOTICE" ];

	/// <summary>
	///    All commonly used file extensions
	/// </summary>
	private static HashSet< string > FileExtensions { get; } = [ string.Empty, ".md", ".txt" ];

	/// <summary>
	///    Attempt to retrieve content of LICENSE file
	/// </summary>
	public static string? GetLicenseFile( string? rootPath, string? licenseFilePath )
	{
		return FileResolver.GetFile( rootPath, licenseFilePath, FileResolver.LicenseFileNames, FileResolver.FileExtensions );
	}

	/// <summary>
	///    Attempt to retrieve content of NOTICE file
	/// </summary>
	public static string? GetNoticeFile( string? rootPath )
	{
		return FileResolver.GetFile( rootPath, null, FileResolver.NoticeFileNames, FileResolver.FileExtensions );
	}

	/// <summary>
	///    Attempt to retrieve content of a file
	/// </summary>
	private static string? GetFile( string? rootPath, string? filePath, IEnumerable< string > fileNames, HashSet< string > fileExtensions )
	{
		ArgumentException.ThrowIfNullOrEmpty( rootPath );

		if( filePath.IsNotEmpty() )
		{
			filePath = Path.Combine( rootPath, filePath );
			if( File.Exists( filePath ) )
			{
				return File.ReadAllText( filePath );
			}
		}

		foreach( string fFileName in fileNames )
		{
			foreach( string fFileExt in fileExtensions )
			{
				filePath = Path.Combine( rootPath, fFileName + fFileExt );
				if( File.Exists( filePath ) )
				{
					return File.ReadAllText( filePath );
				}
			}
		}

		return null;
	}
}
