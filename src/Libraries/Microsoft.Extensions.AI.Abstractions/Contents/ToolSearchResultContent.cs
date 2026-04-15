// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of a tool search, containing the tools discovered by the search.
/// </summary>
/// <remarks>
/// <para>
/// This content type represents the results of a tool search operation, either from a hosted service
/// or from a client-side <see cref="ToolSearchFunction"/>. The discovered tools are available via
/// the <see cref="Tools"/> property.
/// </para>
/// <para>
/// This type extends <see cref="FunctionResultContent"/> to pair naturally with
/// <see cref="ToolSearchCallContent"/>, which extends <see cref="FunctionCallContent"/>.
/// </para>
/// </remarks>
public class ToolSearchResultContent : FunctionResultContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSearchResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID for which this is the result.</param>
    public ToolSearchResultContent(string callId)
        : base(callId, "tool_search")
    {
    }

    /// <summary>
    /// Gets or sets the tools discovered by the search.
    /// </summary>
    public IList<AITool>? Tools { get; set; }
}
