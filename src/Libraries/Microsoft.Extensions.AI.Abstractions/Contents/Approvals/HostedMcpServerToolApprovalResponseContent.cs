// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a response to a MCP server tool request.
/// </summary>
public class HostedMcpServerToolApprovalResponseContent : UserInputResponseContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerToolApprovalResponseContent"/> class.
    /// </summary>
    public HostedMcpServerToolApprovalResponseContent()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HostedMcpServerToolApprovalResponseContent"/> class with the specified approval status.
    /// </summary>
    /// <param name="approved">Indicates whether the request was approved.</param>
    public HostedMcpServerToolApprovalResponseContent(bool approved)
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
    public HostedMcpServerToolCallContent? ToolCall { get; set; }
}
