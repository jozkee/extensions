// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.AI;

public class HostedCodeInterpreterToolTests
{
    [Fact]
    public void Constructor_Roundtrips()
    {
        var tool = new HostedCodeInterpreterTool();
        Assert.Equal("code_interpreter", tool.Name);
        Assert.Empty(tool.Description);
        Assert.Empty(tool.AdditionalProperties);
        Assert.Null(tool.Inputs);
        Assert.Equal(tool.Name, tool.ToString());
    }

    [Fact]
    public void Constructor_AdditionalProperties_Roundtrips()
    {
        var props = new Dictionary<string, object?> { ["key"] = "value" };
        var tool = new HostedCodeInterpreterTool(props);

        Assert.Equal("code_interpreter", tool.Name);
        Assert.Same(props, tool.AdditionalProperties);
    }

    [Fact]
    public void Constructor_NullAdditionalProperties_UsesEmpty()
    {
        var tool = new HostedCodeInterpreterTool(null);

        Assert.Empty(tool.AdditionalProperties);
    }

    [Fact]
    public void Inputs_Roundtrip()
    {
        IList<AIContent> inputs = [new HostedFileContent("file-123")];
        var tool = new HostedCodeInterpreterTool
        {
            Inputs = inputs,
        };

        Assert.Same(inputs, tool.Inputs);
    }

    [Fact]
    public void ExistingContainerInfo_Roundtrips()
    {
        var container = ContainerInfo.FromExisting("container-123");

        Assert.Equal("container-123", container.ContainerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ExistingContainerInfo_ContainerId_Invalid_Throws(string containerId)
    {
        Assert.Throws<ArgumentException>(nameof(containerId), () => ContainerInfo.FromExisting(containerId));
        Assert.Throws<ArgumentException>("value", () => new ExistingContainerInfo("container-123").ContainerId = containerId);
    }
}
