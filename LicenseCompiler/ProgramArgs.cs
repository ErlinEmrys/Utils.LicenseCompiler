using CommandLine;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Command line arguments
/// </summary>
public class ProgramArgs
{
	/// <summary>
	///    Path to solution for which will be licenses generated
	/// </summary>
	[ Option( 's', HelpText = "Path to the main solution" ) ]
	public required string SolutionPath { get; set; }

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
}
