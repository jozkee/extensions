// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.Extensions.AI;

public class HostedMcpServerToolApprovalModeTests
{
    [Fact]
    public void Singletons_Idempotent()
    {
        Assert.Same(HostedMcpServerToolApprovalMode.AlwaysRequire, HostedMcpServerToolApprovalMode.AlwaysRequire);
        Assert.Same(HostedMcpServerToolApprovalMode.NeverRequire, HostedMcpServerToolApprovalMode.NeverRequire);
    }

    [Fact]
    public void StaticProperties()
    {
        HostedMcpServerToolApprovalMode always = HostedMcpServerToolApprovalMode.Always;
        Assert.NotNull(always);
        Assert.Same(always, HostedMcpServerToolApprovalMode.Always);
        Assert.Null(always.Require);
        Assert.Null(always.NotRequire);

        HostedMcpServerToolApprovalMode never = HostedMcpServerToolApprovalMode.Never;
        Assert.NotNull(never);
        Assert.Same(never, HostedMcpServerToolApprovalMode.Never);
        Assert.Null(never.Require);
        Assert.Null(never.NotRequire);

        Assert.NotSame(always, never);
    }
}
