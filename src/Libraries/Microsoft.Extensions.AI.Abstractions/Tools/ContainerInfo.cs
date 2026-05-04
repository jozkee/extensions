// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>Describes the hosted container to use for container-aware tool calls.</summary>
[Experimental(DiagnosticIds.Experiments.AIContainers, UrlFormat = DiagnosticIds.UrlFormat)]
public abstract class ContainerInfo
{
    /// <summary>Initializes a new instance of the <see cref="ContainerInfo"/> class.</summary>
    private protected ContainerInfo()
    {
    }

    /// <summary>Creates a <see cref="ContainerInfo"/> instance that requests reuse of an existing hosted container.</summary>
    /// <param name="containerId">The ID of the hosted container to reuse.</param>
    /// <returns>An <see cref="ExistingContainerInfo"/> instance for <paramref name="containerId"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="containerId"/> is empty or composed entirely of whitespace.</exception>
    public static ExistingContainerInfo FromExisting(string containerId) => new(containerId);
}
