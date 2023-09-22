// Assembly 'Microsoft.Extensions.Compliance.Testing'

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.Compliance.Testing;

/// <summary>
/// Options to control the fake redactor.
/// </summary>
public class FakeRedactorOptions
{
    /// <summary>
    /// Gets or sets a value indicating how to format redacted data.
    /// </summary>
    /// <value>The default is "{0}".</value>
    /// <remarks>
    /// This is a composite format string that determines how redacted data looks.
    /// </remarks>
    [Required]
    [StringSyntax("CompositeFormat")]
    public string RedactionFormat { get; set; }

    public FakeRedactorOptions();
}
