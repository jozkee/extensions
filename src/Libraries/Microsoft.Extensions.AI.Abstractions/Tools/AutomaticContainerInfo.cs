// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>Indicates that the hosted container should be managed automatically by the service.</summary>
/// <remarks>
/// Selecting <see cref="AutomaticContainerInfo"/> tells the service to manage the container lifetime. Some services
/// always provision a fresh container in this mode, while others reuse a container associated with the current
/// conversation or message history. Adapter implementations may also use this option to opt in to history-based
/// container reuse, lifting the most recent <see cref="CodeInterpreterToolCallContent.ContainerId"/> from the
/// conversation when present.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class AutomaticContainerInfo : ContainerInfo
{
    /// <summary>Initializes a new instance of the <see cref="AutomaticContainerInfo"/> class.</summary>
    public AutomaticContainerInfo()
    {
    }
}
