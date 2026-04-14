// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolSearchResultContentTests
{
    [Fact]
    public void Constructor_PropsDefault()
    {
        ToolSearchResultContent c = new("callId");
        Assert.Equal("callId", c.CallId);
        Assert.Equal("tool_search", c.Result);
        Assert.Null(c.Tools);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
    }

    [Fact]
    public void IsInstanceOf_FunctionResultContent()
    {
        ToolSearchResultContent c = new("callId");

        Assert.IsAssignableFrom<FunctionResultContent>(c);
        Assert.IsAssignableFrom<ToolResultContent>(c);
        Assert.IsAssignableFrom<AIContent>(c);
    }

    [Fact]
    public void Properties_Roundtrip()
    {
        ToolSearchResultContent c = new("ts_result123");

        Assert.Equal("ts_result123", c.CallId);
        Assert.Equal("tool_search", c.Result);

        Assert.Null(c.Tools);
        var tools = new List<AITool> { new HostedWebSearchTool() };
        c.Tools = tools;
        Assert.Same(tools, c.Tools);

        Assert.Null(c.RawRepresentation);
        object raw = new();
        c.RawRepresentation = raw;
        Assert.Same(raw, c.RawRepresentation);

        Assert.Null(c.AdditionalProperties);
        AdditionalPropertiesDictionary props = new() { { "key", "value" } };
        c.AdditionalProperties = props;
        Assert.Same(props, c.AdditionalProperties);
    }

    [Fact]
    public void Tools_SupportsMultipleItems()
    {
        ToolSearchResultContent c = new("ts_result789")
        {
            Tools =
            [
                new HostedWebSearchTool(),
                new HostedToolSearchTool(),
            ]
        };

        Assert.NotNull(c.Tools);
        Assert.Equal(2, c.Tools.Count);
        Assert.IsType<HostedWebSearchTool>(c.Tools[0]);
        Assert.IsType<HostedToolSearchTool>(c.Tools[1]);
    }

    [Fact]
    public void Serialization_Roundtrips()
    {
        ToolSearchResultContent content = new("ts_result123");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolSearchResultContent>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("ts_result123", deserialized.CallId);
        Assert.Equal("tool_search", deserialized.Result?.ToString());
    }

    [Fact]
    public void Serialization_AsAIContent_Roundtrips()
    {
        AIContent content = new ToolSearchResultContent("ts_result456");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AIContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchResultContent>(deserialized);
        Assert.Equal("ts_result456", result.CallId);
    }

    [Fact]
    public void Serialization_AsToolResultContent_Roundtrips()
    {
        ToolResultContent content = new ToolSearchResultContent("ts_result789");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolResultContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchResultContent>(deserialized);
        Assert.Equal("ts_result789", result.CallId);
    }

    [Fact]
    public void Serialization_AsFunctionResultContent_Roundtrips()
    {
        FunctionResultContent content = new ToolSearchResultContent("ts_result_frc");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FunctionResultContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchResultContent>(deserialized);
        Assert.Equal("ts_result_frc", result.CallId);
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "toolSearchResult",
              "callId": "ts-result1",
              "result": "tool_search",
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var tsResult = Assert.IsType<ToolSearchResultContent>(result);
        Assert.Equal("ts-result1", tsResult.CallId);
        Assert.NotNull(tsResult.AdditionalProperties);
        Assert.Equal("val", tsResult.AdditionalProperties["key"]?.ToString());
    }
}
