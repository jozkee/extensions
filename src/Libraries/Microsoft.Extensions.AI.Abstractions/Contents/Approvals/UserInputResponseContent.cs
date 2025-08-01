// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

#pragma warning disable S1694 // An abstract class should have both abstract and concrete methods

/// <summary>
/// Base class for user input response content.
/// </summary>
public abstract class UserInputResponseContent : AIContent
{
    /// <summary>
    /// Gets or sets the ID to uniquely identify the user input request/response pair.
    /// </summary>
    public string ApprovalId { get; set; } = default!;
}
