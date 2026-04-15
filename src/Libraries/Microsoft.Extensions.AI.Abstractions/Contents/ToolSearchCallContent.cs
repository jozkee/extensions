// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a tool search call request from a model or hosted service.
/// </summary>
/// <remarks>
/// <para>
/// This content type represents when a model or hosted AI service invokes a tool search,
/// requesting that tools matching the search criteria be loaded. It extends <see cref="FunctionCallContent"/>
/// because the wire format has the same shape (call ID, name, arguments), enabling existing middleware
/// that processes <see cref="FunctionCallContent"/> to work without changes.
/// </para>
/// <para>
/// The result of a tool search call is represented by <see cref="ToolSearchResultContent"/>.
/// </para>
/// </remarks>
public class ToolSearchCallContent : FunctionCallContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSearchCallContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    /// <param name="name">The tool search function name.</param>
    /// <param name="arguments">The tool search arguments.</param>
    public ToolSearchCallContent(string callId, string name, IDictionary<string, object?>? arguments = null)
        : base(callId, name, arguments)
    {
    }
}
