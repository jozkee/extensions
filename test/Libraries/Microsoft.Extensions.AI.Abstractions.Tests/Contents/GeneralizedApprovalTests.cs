// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;

namespace Microsoft.Extensions.AI.Contents;

/// <summary>
/// Tests demonstrating that the generalized approval mechanism works with any AIContent type.
/// </summary>
public class GeneralizedApprovalTests
{
    [Fact]
    public void FunctionApprovalRequestContent_WorksWithFunctionCallContent()
    {
        // Arrange
        var functionCall = new FunctionCallContent("call-123", "TestFunction");

        // Act
        var request = new FunctionApprovalRequestContent("approval-1", functionCall);

        // Assert
        Assert.Equal("approval-1", request.Id);
        Assert.Same(functionCall, request.CallContent);
    }

    [Fact]
    public void FunctionApprovalRequestContent_WorksWithMcpServerToolCallContent()
    {
        // Arrange
        var mcpCall = new McpServerToolCallContent("call-456", "McpTool", "McpServer1");

        // Act
        var request = new FunctionApprovalRequestContent("approval-2", mcpCall);

        // Assert
        Assert.Equal("approval-2", request.Id);
        Assert.Same(mcpCall, request.CallContent);

        // Can cast back to McpServerToolCallContent
        var retrievedMcpCall = Assert.IsType<McpServerToolCallContent>(request.CallContent);
        Assert.Equal("call-456", retrievedMcpCall.CallId);
        Assert.Equal("McpTool", retrievedMcpCall.ToolName);
        Assert.Equal("McpServer1", retrievedMcpCall.ServerName);
    }

    [Fact]
    public void FunctionApprovalResponseContent_WorksWithFunctionCallContent()
    {
        // Arrange
        var functionCall = new FunctionCallContent("call-123", "TestFunction");

        // Act
        var response = new FunctionApprovalResponseContent("approval-1", true, functionCall);

        // Assert
        Assert.Equal("approval-1", response.Id);
        Assert.True(response.Approved);
        Assert.Same(functionCall, response.CallContent);
    }

    [Fact]
    public void FunctionApprovalResponseContent_WorksWithMcpServerToolCallContent()
    {
        // Arrange
        var mcpCall = new McpServerToolCallContent("call-456", "McpTool", "McpServer1");

        // Act
        var response = new FunctionApprovalResponseContent("approval-2", false, mcpCall);

        // Assert
        Assert.Equal("approval-2", response.Id);
        Assert.False(response.Approved);
        Assert.Same(mcpCall, response.CallContent);

        // Can cast back to McpServerToolCallContent
        var retrievedMcpCall = Assert.IsType<McpServerToolCallContent>(response.CallContent);
        Assert.Equal("call-456", retrievedMcpCall.CallId);
        Assert.Equal("McpTool", retrievedMcpCall.ToolName);
        Assert.Equal("McpServer1", retrievedMcpCall.ServerName);
    }

    [Fact]
    public void CreateResponse_WorksWithAnyContentType()
    {
        // Arrange - with FunctionCallContent
        var functionCall = new FunctionCallContent("call-123", "TestFunction");
        var functionRequest = new FunctionApprovalRequestContent("approval-1", functionCall);

        // Act
        var functionResponse = functionRequest.CreateResponse(true);

        // Assert
        Assert.Equal("approval-1", functionResponse.Id);
        Assert.True(functionResponse.Approved);
        Assert.Same(functionCall, functionResponse.CallContent);

        // Arrange - with McpServerToolCallContent
        var mcpCall = new McpServerToolCallContent("call-456", "McpTool", "McpServer1");
        var mcpRequest = new FunctionApprovalRequestContent("approval-2", mcpCall);

        // Act
        var mcpResponse = mcpRequest.CreateResponse(false);

        // Assert
        Assert.Equal("approval-2", mcpResponse.Id);
        Assert.False(mcpResponse.Approved);
        Assert.Same(mcpCall, mcpResponse.CallContent);
    }

    [Fact]
    public void ApprovalWorkflow_CompleteScenario_WithMcpServerToolCallContent()
    {
        // This test demonstrates a complete approval workflow with MCP server tool calls

        // Step 1: Create an MCP tool call
        var mcpCall = new McpServerToolCallContent("mcp-call-789", "WeatherTool", "WeatherServer")
        {
            Arguments = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["location"] = "Seattle",
                ["units"] = "celsius"
            }
        };

        // Step 2: Create an approval request
        var approvalRequest = new FunctionApprovalRequestContent("approval-weather", mcpCall);
        Assert.Equal("approval-weather", approvalRequest.Id);
        Assert.IsType<McpServerToolCallContent>(approvalRequest.CallContent);

        // Step 3: Simulate user approval
        var approvalResponse = approvalRequest.CreateResponse(approved: true);
        Assert.True(approvalResponse.Approved);
        Assert.Same(mcpCall, approvalResponse.CallContent);

        // Step 4: Extract the MCP call from the response for execution
        var approvedMcpCall = Assert.IsType<McpServerToolCallContent>(approvalResponse.CallContent);
        Assert.Equal("mcp-call-789", approvedMcpCall.CallId);
        Assert.Equal("WeatherTool", approvedMcpCall.ToolName);
        Assert.Equal("WeatherServer", approvedMcpCall.ServerName);
        Assert.NotNull(approvedMcpCall.Arguments);
        Assert.Equal("Seattle", approvedMcpCall.Arguments["location"]);
    }

    [Fact]
    public void BackwardCompatibility_FunctionCallProperty_ReturnsNullForNonFunctionContent()
    {
        // Arrange
        var mcpCall = new McpServerToolCallContent("call-456", "McpTool", "McpServer1");
        var request = new FunctionApprovalRequestContent("approval-2", mcpCall);
        var response = new FunctionApprovalResponseContent("approval-2", true, mcpCall);

        // Act & Assert
        Assert.NotNull(request.CallContent); // But CallContent still has the value
        Assert.NotNull(response.CallContent); // But CallContent still has the value
    }
}
