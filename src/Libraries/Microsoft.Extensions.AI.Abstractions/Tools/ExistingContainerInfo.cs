// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Describes an existing hosted container to reuse for code interpreter tool calls.</summary>
[Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class ExistingContainerInfo : ContainerInfo
{
    /// <summary>Initializes a new instance of the <see cref="ExistingContainerInfo"/> class.</summary>
    /// <param name="containerId">The ID of the hosted container to reuse.</param>
    /// <exception cref="ArgumentException"><paramref name="containerId"/> is empty or composed entirely of whitespace.</exception>
    public ExistingContainerInfo(string containerId)
    {
        ContainerId = Throw.IfNullOrWhitespace(containerId);
    }

    /// <summary>Gets or sets the ID of the hosted container to reuse.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or composed entirely of whitespace.</exception>
    public string ContainerId
    {
        get;
        set => field = Throw.IfNullOrWhitespace(value);
    }
}
