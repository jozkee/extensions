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
        Assert.Null(tool.Container);
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
    public void Properties_Roundtrip()
    {
        var tool = new HostedCodeInterpreterTool
        {
            Container = ContainerInfo.FromExisting("container-123"),
        };

        var container = Assert.IsType<ExistingContainerInfo>(tool.Container);
        Assert.Equal("container-123", container.ContainerId);
    }

    [Fact]
    public void CreateNewContainerInfo_Roundtrips()
    {
        IList<AIContent> inputs =
        [
            new HostedFileContent("id123"),
            new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")
        ];

        var container = ContainerInfo.CreateNew(inputs);

        Assert.Same(inputs, container.Inputs);
        Assert.IsType<HostedFileContent>(container.Inputs![0]);
        Assert.IsType<DataContent>(container.Inputs[1]);
    }

    [Fact]
    public void Clone_ReturnsShallowCopy()
    {
        var props = new Dictionary<string, object?> { ["key"] = "value" };
        List<AIContent> inputs = [new HostedFileContent("id123")];
        var tool = new HostedCodeInterpreterTool(props)
        {
            Container = ContainerInfo.CreateNew(inputs),
        };

        var clone = tool.Clone();

        Assert.NotSame(tool, clone);
        Assert.IsType<HostedCodeInterpreterTool>(clone);
        var container = Assert.IsType<CreateNewContainerInfo>(clone.Container);
        Assert.Same(inputs, container.Inputs);
        Assert.Same(props, clone.AdditionalProperties);
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
