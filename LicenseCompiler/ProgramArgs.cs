using CommandLine;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Command line arguments
/// </summary>
public class ProgramArgs
{
	/// <summary>
	///    Path to solution file for which will be licenses generated
	/// </summary>
	[ Option( 's', HelpText = "Path to the solution file" ) ]
	public required string SolutionFilePath { get; set; }

	/// <summary>
	///    Path to soultion folder
	/// </summary>
	public string SolutionPath
	{
		get { return Path.GetDirectoryName( SolutionFilePath ) ?? throw new InvalidOperationException( "Solution path is not set" ); }
	}

	/// <summary>
	///    Path for output JSON file
	/// </summary>
	[ Option( "oj", HelpText = "Path to output json file" ) ]
	public string? OutputJsonPath { get; set; }

	/// <summary>
	///    Whether the output JSON file should be human readable
	/// </summary>
	[ Option( "oji", HelpText = "Makes output json file indented" ) ]
	public bool OutputJsonIndented { get; set; }

	/// <summary>
	///    Path for output MD file
	/// </summary>
	[ Option( "om", Default = "LICENSE_THIRD_PARTIES.md", HelpText = "Path to output MD file" ) ]
	public string? OutputMDPath { get; set; }

	/// <summary>
	///    Whether the program should be writing more info to the log
	/// </summary>
	[ Option( "lv", HelpText = "Rise log level to be more verbose" ) ]
	public bool LogVerbose { get; set; }

	/// <summary>
	///    Whether the program should be writing log to file
	/// </summary>
	[ Option( "lf", HelpText = "Write log to file" ) ]
	public bool LogToFile { get; set; }

	/// <summary>
	///    Whether the program should be running synchronsously during the build
	/// </summary>
	[ Option( "sync", HelpText = "Run synchronsously during the build" ) ]
	public bool SynchronousRun { get; set; }
}
