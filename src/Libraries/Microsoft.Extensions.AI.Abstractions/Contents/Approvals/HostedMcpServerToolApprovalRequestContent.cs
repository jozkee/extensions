// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a request for user approval of a MCP server tool.
/// </summary>
public class HostedMcpServerToolApprovalRequestContent : UserInputRequestContent
{
    /// <summary>
    /// Gets or sets the function call that pre-invoke approval is required for.
    /// </summary>
    public HostedMcpServerToolCallContent? ToolCall { get; set; }

    /// <summary>
    /// Creates a <see cref="ChatMessage"/> representing an approval response.
    /// </summary>
    /// <returns>The <see cref="ChatMessage"/> representing the approval response.</returns>
    public ChatMessage Approve()
    {
        return new ChatMessage(ChatRole.Tool,
        [
            new HostedMcpServerToolApprovalResponseContent
            {
                ApprovalId = ApprovalId,
                Approved = true,
                ToolCall = ToolCall
            }
        ]);
    }

    /// <summary>
    /// Creates a <see cref="ChatMessage"/> representing a rejection response.
    /// </summary>
    /// <returns>The <see cref="ChatMessage"/> representing the rejection response.</returns>
    public ChatMessage Reject()
    {
        return new ChatMessage(ChatRole.Tool,
        [
            new HostedMcpServerToolApprovalResponseContent
            {
                ApprovalId = ApprovalId,
                Approved = false,
                ToolCall = ToolCall
            }
        ]);
    }
}
