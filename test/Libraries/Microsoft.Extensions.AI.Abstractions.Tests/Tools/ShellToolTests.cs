// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ShellToolTests
{
    [Fact]
    public void Constructor_Roundtrips()
    {
        var tool = new ShellTool();
        Assert.Equal("local_shell", tool.Name);
        Assert.Equal("Executes a shell command and returns stdout, stderr, and exit code.", tool.Description);
        Assert.Empty(tool.AdditionalProperties);
        Assert.Equal(tool.Name, tool.ToString());
    }

    [Fact]
    public void Constructor_AdditionalProperties_Roundtrips()
    {
        var props = new Dictionary<string, object?> { ["key"] = "value" };
        var tool = new ShellTool(props);

        Assert.Equal("local_shell", tool.Name);
        Assert.Same(props, tool.AdditionalProperties);
    }

    [Fact]
    public void JsonSchema_DefinesCommandParameter()
    {
        var tool = new ShellTool();
        var schema = tool.JsonSchema;

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("command", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("timeout_ms", out _));
        Assert.Contains("command", schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public async Task InvokeAsync_ExecutesCommand_ReturnsOutput()
    {
        var tool = new ShellTool();
        var arguments = new AIFunctionArguments { ["command"] = "echo hello" };

        var result = await tool.InvokeAsync(arguments);

        var resultString = Assert.IsType<string>(result);
        Assert.Contains("hello", resultString);
        Assert.Contains("Exit Code: 0", resultString);
    }
}
