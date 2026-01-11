namespace Erlin.Utils.LicenseCompiler;

/// <summary>
///    Data type of the licenses
/// </summary>
public enum LicenseDataType
{
	/// <summary>
	///    Enum error
	/// </summary>
	EnumNullError = 0,

	/// <summary>
	///    License error
	/// </summary>
	Error = 1,

	/// <summary>
	///    Plain text
	/// </summary>
	Text = 2,

	/// <summary>
	///    Short expression
	/// </summary>
	Expression = 3,

	/// <summary>
	///    Web url
	/// </summary>
	Url = 4
}
