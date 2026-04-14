// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolSearchFunctionTests
{
    private static readonly JsonElement _testSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" }
          },
          "required": ["query"]
        }
        """).RootElement;

    [Fact]
    public void Constructor_Async_NullDescription_Throws()
    {
        Assert.Throws<ArgumentNullException>("description", () => new ToolSearchFunction(
            null!,
            _testSchema,
            (args, ct) => new ValueTask<IList<AITool>>(Array.Empty<AITool>())));
    }

    [Fact]
    public void Constructor_Async_NullSearchFunc_Throws()
    {
        Assert.Throws<ArgumentNullException>("searchFunc", () => new ToolSearchFunction(
            "description",
            _testSchema,
            (Func<AIFunctionArguments, CancellationToken, ValueTask<IList<AITool>>>)null!));
    }

    [Fact]
    public void Constructor_Sync_NullDescription_Throws()
    {
        Assert.Throws<ArgumentNullException>("description", () => new ToolSearchFunction(
            null!,
            _testSchema,
            (args) => Array.Empty<AITool>()));
    }

    [Fact]
    public void Constructor_Sync_NullSearchFunc_Throws()
    {
        Assert.Throws<ArgumentNullException>("searchFunc", () => new ToolSearchFunction(
            "description",
            _testSchema,
            (Func<AIFunctionArguments, IList<AITool>>)null!));
    }

    [Fact]
    public void Constructor_Async_DefaultName()
    {
        var func = new ToolSearchFunction(
            "Search for tools",
            _testSchema,
            (args, ct) => new ValueTask<IList<AITool>>(Array.Empty<AITool>()));

        Assert.Equal("tool_search", func.Name);
        Assert.Equal("Search for tools", func.Description);
        Assert.Equal(_testSchema, func.JsonSchema);
    }

    [Fact]
    public void Constructor_Async_CustomName()
    {
        var func = new ToolSearchFunction(
            "Search for tools",
            _testSchema,
            (args, ct) => new ValueTask<IList<AITool>>(Array.Empty<AITool>()),
            name: "my_search");

        Assert.Equal("my_search", func.Name);
    }

    [Fact]
    public void Constructor_Sync_DefaultName()
    {
        var func = new ToolSearchFunction(
            "Search for tools",
            _testSchema,
            (args) => Array.Empty<AITool>());

        Assert.Equal("tool_search", func.Name);
        Assert.Equal("Search for tools", func.Description);
        Assert.Equal(_testSchema, func.JsonSchema);
    }

    [Fact]
    public void Constructor_Sync_CustomName()
    {
        var func = new ToolSearchFunction(
            "Search for tools",
            _testSchema,
            (args) => Array.Empty<AITool>(),
            name: "custom_search");

        Assert.Equal("custom_search", func.Name);
    }

    [Fact]
    public void IsInstanceOf_AIFunction()
    {
        var func = new ToolSearchFunction(
            "desc",
            _testSchema,
            (args) => Array.Empty<AITool>());

        Assert.IsAssignableFrom<AIFunction>(func);
        Assert.IsAssignableFrom<AIFunctionDeclaration>(func);
        Assert.IsAssignableFrom<AITool>(func);
    }

    [Fact]
    public async Task InvokeAsync_Sync_ReturnsToolList()
    {
        var expectedTools = new List<AITool> { new HostedWebSearchTool() };
        var func = new ToolSearchFunction(
            "Search tools",
            _testSchema,
            (args) => expectedTools);

        var result = await func.InvokeAsync(new AIFunctionArguments { ["query"] = "web search" });

        var tools = Assert.IsAssignableFrom<IList<AITool>>(result);
        Assert.Same(expectedTools, tools);
    }

    [Fact]
    public async Task InvokeAsync_Async_ReturnsToolList()
    {
        var expectedTools = new List<AITool> { new HostedWebSearchTool(), new HostedToolSearchTool() };
        var func = new ToolSearchFunction(
            "Search tools",
            _testSchema,
            async (args, ct) =>
            {
                await Task.Yield();
                return expectedTools;
            });

        var result = await func.InvokeAsync(new AIFunctionArguments { ["query"] = "tools" });

        var tools = Assert.IsAssignableFrom<IList<AITool>>(result);
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task InvokeAsync_PassesArguments()
    {
        string? capturedQuery = null;
        var func = new ToolSearchFunction(
            "Search tools",
            _testSchema,
            (args) =>
            {
                capturedQuery = args["query"]?.ToString();
                return Array.Empty<AITool>();
            });

        await func.InvokeAsync(new AIFunctionArguments { ["query"] = "find email tools" });

        Assert.Equal("find email tools", capturedQuery);
    }

    [Fact]
    public async Task InvokeAsync_Async_PassesCancellationToken()
    {
        CancellationToken capturedToken = default;
        var func = new ToolSearchFunction(
            "Search tools",
            _testSchema,
            (args, ct) =>
            {
                capturedToken = ct;
                return new ValueTask<IList<AITool>>(Array.Empty<AITool>());
            });

        using var cts = new CancellationTokenSource();
        await func.InvokeAsync(new AIFunctionArguments { ["query"] = "test" }, cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }

    [Fact]
    public void JsonSchema_ReturnsProvidedSchema()
    {
        var customSchema = JsonDocument.Parse("""{ "type": "object", "properties": { "keyword": { "type": "string" } } }""").RootElement;

        var func = new ToolSearchFunction(
            "desc",
            customSchema,
            (args) => Array.Empty<AITool>());

        Assert.Equal(customSchema, func.JsonSchema);
    }
}
