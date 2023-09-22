// Assembly 'Microsoft.Extensions.Telemetry'

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.Diagnostics.Enrichment;

/// <summary>
/// Constants used for enrichment tags.
/// </summary>
public static class ProcessEnricherTagNames
{
    /// <summary>
    /// Process ID.
    /// </summary>
    public const string ProcessId = "pid";

    /// <summary>
    /// Thread ID.
    /// </summary>
    public const string ThreadId = "tid";

    /// <summary>
    /// Gets a list of all dimension names.
    /// </summary>
    /// <returns>A read-only <see cref="T:System.Collections.Generic.IReadOnlyList`1" /> of all dimension names.</returns>
    public static IReadOnlyList<string> DimensionNames { get; }
}
