using System.Diagnostics;
using System.Globalization;

using CommandLine;

using Erlin.Lib.Common;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting;

using Log = Erlin.Lib.Common.Log;

namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Main program
/// </summary>
public static class Program
{
	public const int PRG_EXIT_OK = 0;
	public const int PRG_EXIT_APPLICATION_ERROR = 100;
	public const int PRG_EXIT_LOG_INIT = 200;
	public const int PRG_EXIT_CONSOLE_ERROR = 300;
	public const int PRG_EXIT_LOG_FATAL = 400;
	public const int PRG_EXIT_ARGUMENTS_ERROR = 500;
	public const int PRG_EXIT_PACKAGES_ERROR = 600;

	/// <summary>
	///    Entry point
	/// </summary>
	/// <param name="args">Command line arguments</param>
	public static async Task< int > Main( string[] args )
	{
		try
		{
			return await Program.Run( args );
		}
		catch( Exception e )
		{
			try
			{
				await Console.Error.WriteLineAsync( $"Critical unhandled exception {e}" );

				if( Debugger.IsAttached )
				{
					Debugger.Break();
				}

				return PRG_EXIT_LOG_INIT;
			}
			catch
			{
				return PRG_EXIT_CONSOLE_ERROR;
			}
		}
		finally
		{
			if( Debugger.IsAttached )
			{
				Console.WriteLine( "Press any key to continue..." );
				Console.ReadLine();
			}
		}
	}

	/// <summary>
	///    Logging and error handling
	/// </summary>
	private static async Task< int > Run( IEnumerable< string > args )
	{
		LoggingLevelSwitch logLevelSwitch = new();
		logLevelSwitch.MinimumLevel = LogEventLevel.Information;

#if DEBUG
		logLevelSwitch.MinimumLevel = LogEventLevel.Debug;
#endif

		LoggerConfiguration logConfig = new();
		Program.ConfigureLogging( logConfig, logLevelSwitch, false );

		ReloadableLogger logger = logConfig.CreateBootstrapLogger();
		Log.Initialize( logger );
		Log.Dbg( "APP START" );

		bool appRunning = false;

		try
		{
			ParserResult< ProgramArgs > parsedArgs = Parser.Default.ParseArguments< ProgramArgs >( args );
			return await parsedArgs.MapResult( a =>
			{
				try
				{
					if( a.SolutionFilePath.IsEmpty() )
					{
						string path = Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), @"..\..\..\" ) );

						string? sln = Directory.GetFiles( path, "*.sln", SearchOption.TopDirectoryOnly ).FirstOrDefault();

						a.SolutionFilePath = sln;
					}

					Directory.SetCurrentDirectory( a.SolutionPath );

					if( a.LogVerbose )
					{
						logLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
					}

					if( a.LogToFile )
					{
						Program.EnableFileLog( logger, logLevelSwitch );
					}

					if( a.SynchronousRun )
					{
						Log.Dbg( "Synchronous run" );
					}

					appRunning = true;
					return Program.RunApp( a );
				}
				catch( Exception err )
				{
					Program.EnableFileLog( logger, logLevelSwitch );

					Log.Fatal( err );
					return Task.FromResult( PRG_EXIT_APPLICATION_ERROR );
				}
			}, errors =>
			{
				Program.EnableFileLog( logger, logLevelSwitch );

				foreach( Error fArgError in errors )
				{
					switch( fArgError )
					{
						case TokenError tokenError:
							Log.Err( "Command line argument error: {Token} {Tag}", tokenError.Token, fArgError.Tag );
							break;

						case NamedError namedError:
							Log.Err( "Command line argument error: {Name} {Tag}", namedError.NameInfo.NameText, fArgError.Tag );
							break;

						default:
							Log.Err( "Command line argument error: {Tag}", fArgError.Tag );
							break;
					}
				}

				return Task.FromResult( PRG_EXIT_ARGUMENTS_ERROR );
			} );
		}
		catch( Exception e )
		{
			Program.EnableFileLog( logger, logLevelSwitch );

			if( appRunning )
			{
				Log.Err( e );
				return PRG_EXIT_APPLICATION_ERROR;
			}

			Log.Fatal( e );
			return PRG_EXIT_LOG_FATAL;
		}
		finally
		{
			Log.Dbg( "APP END" );
			await Log.DisposeAsync();
		}
	}

	private static LoggerConfiguration ConfigureLogging( LoggerConfiguration logConfig, LoggingLevelSwitch logLevelSwitch, bool enableFileLog )
	{
		logConfig = logConfig.MinimumLevel.ControlledBy( logLevelSwitch )
								.WriteTo.Console( theme: Log.DefaultConsoleColorTheme, outputTemplate: Log.DefaultOutputTemplate, formatProvider: CultureInfo.InvariantCulture )
								.Enrich.With< ExceptionLogEnricher >();

		if( enableFileLog )
		{
			logConfig = logConfig.WriteTo.File( Path.Combine( Directory.GetCurrentDirectory(), "Erlin.Utils.LicenseCompiler_Log_.txt" ), outputTemplate: Log.DefaultOutputTemplate, rollingInterval: RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture );
		}

		return logConfig;
	}

	private static void EnableFileLog( ReloadableLogger logger, LoggingLevelSwitch logLevelSwitch )
	{
		try
		{
			logger.Reload( config => Program.ConfigureLogging( config, logLevelSwitch, true ) );
			Log.Initialize( logger );
			Log.Dbg( "File log enabled" );
		}
		catch( Exception e )
		{
			Log.Err( e, "Failed to enable file log" );
		}
	}

	/// <summary>
	///    Application
	/// </summary>
	private static async Task< int > RunApp( ProgramArgs args )
	{
		GeneratorResult result = await PackageResolver.ResolvePackages( args );

		await OutputWriter.WriteOutputMD( args, result );

		await OutputWriter.WriteOutputJson( args, result );

		return result.Packages.Any( p => p.LicenseDataType is LicenseDataType.EnumNullError or LicenseDataType.Error ) ? PRG_EXIT_PACKAGES_ERROR : PRG_EXIT_OK;
	}
}
