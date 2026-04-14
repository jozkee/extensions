// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.AI;

public class ToolSearchCallContentTests
{
    [Fact]
    public void Constructor_PropsDefault()
    {
        ToolSearchCallContent c = new("callId", "tool_search");
        Assert.Equal("callId", c.CallId);
        Assert.Equal("tool_search", c.Name);
        Assert.Null(c.Arguments);
        Assert.Null(c.RawRepresentation);
        Assert.Null(c.AdditionalProperties);
        Assert.False(c.InformationalOnly);
    }

    [Fact]
    public void Constructor_WithArguments()
    {
        var args = new Dictionary<string, object?> { ["query"] = "weather" };
        ToolSearchCallContent c = new("call123", "tool_search", args);

        Assert.Equal("call123", c.CallId);
        Assert.Equal("tool_search", c.Name);
        Assert.Same(args, c.Arguments);
    }

    [Fact]
    public void IsInstanceOf_FunctionCallContent()
    {
        ToolSearchCallContent c = new("callId", "tool_search");

        Assert.IsAssignableFrom<FunctionCallContent>(c);
        Assert.IsAssignableFrom<ToolCallContent>(c);
        Assert.IsAssignableFrom<AIContent>(c);
    }

    [Fact]
    public void Properties_Roundtrip()
    {
        ToolSearchCallContent c = new("ts_call123", "tool_search");

        Assert.Equal("ts_call123", c.CallId);
        Assert.Equal("tool_search", c.Name);

        Assert.Null(c.Arguments);
        var args = new Dictionary<string, object?> { ["query"] = "search term" };
        c.Arguments = args;
        Assert.Same(args, c.Arguments);

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
    public void Serialization_Roundtrips()
    {
        ToolSearchCallContent content = new("ts_call123", "tool_search", new Dictionary<string, object?> { ["query"] = "weather tools" });

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolSearchCallContent>(json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("ts_call123", deserialized.CallId);
        Assert.Equal("tool_search", deserialized.Name);
        Assert.NotNull(deserialized.Arguments);
    }

    [Fact]
    public void Serialization_AsAIContent_Roundtrips()
    {
        AIContent content = new ToolSearchCallContent("ts_call456", "tool_search", new Dictionary<string, object?> { ["query"] = "email" });

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<AIContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchCallContent>(deserialized);
        Assert.Equal("ts_call456", result.CallId);
        Assert.Equal("tool_search", result.Name);
    }

    [Fact]
    public void Serialization_AsToolCallContent_Roundtrips()
    {
        ToolCallContent content = new ToolSearchCallContent("ts_call789", "tool_search");

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<ToolCallContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchCallContent>(deserialized);
        Assert.Equal("ts_call789", result.CallId);
    }

    [Fact]
    public void Serialization_AsFunctionCallContent_Roundtrips()
    {
        FunctionCallContent content = new ToolSearchCallContent("ts_call_fc", "tool_search", new Dictionary<string, object?> { ["q"] = "test" });

        var json = JsonSerializer.Serialize(content, AIJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<FunctionCallContent>(json, AIJsonUtilities.DefaultOptions);

        var result = Assert.IsType<ToolSearchCallContent>(deserialized);
        Assert.Equal("ts_call_fc", result.CallId);
        Assert.Equal("tool_search", result.Name);
    }

    [Fact]
    public void JsonDeserialization_KnownPayload()
    {
        const string Json = """
            {
              "$type": "toolSearchCall",
              "callId": "ts-call1",
              "name": "tool_search",
              "arguments": {
                "query": "find tools"
              },
              "additionalProperties": {
                "key": "val"
              }
            }
            """;

        AIContent? result = JsonSerializer.Deserialize<AIContent>(Json, AIJsonUtilities.DefaultOptions);

        Assert.NotNull(result);
        var tsCall = Assert.IsType<ToolSearchCallContent>(result);
        Assert.Equal("ts-call1", tsCall.CallId);
        Assert.Equal("tool_search", tsCall.Name);
        Assert.NotNull(tsCall.Arguments);
        Assert.NotNull(tsCall.AdditionalProperties);
        Assert.Equal("val", tsCall.AdditionalProperties["key"]?.ToString());
    }
}
