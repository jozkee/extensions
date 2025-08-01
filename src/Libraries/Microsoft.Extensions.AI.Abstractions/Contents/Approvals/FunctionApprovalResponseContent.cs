// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a response to a function approval request.
/// </summary>
public class FunctionApprovalResponseContent : UserInputResponseContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionApprovalResponseContent"/> class.
    /// </summary>
    public FunctionApprovalResponseContent()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionApprovalResponseContent"/> class with the specified approval status.
    /// </summary>
    /// <param name="approved">Indicates whether the request was approved.</param>
    public FunctionApprovalResponseContent(bool approved)
    {
        Approved = approved;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user approved the request.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// Gets or sets the function call that pre-invoke approval is required for.
    /// </summary>
    public FunctionCallContent FunctionCall { get; set; } = default!;
}
