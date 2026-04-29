// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>Describes a hosted container provisioned automatically by the service for code interpreter tool calls.</summary>
/// <remarks>
/// Selecting <see cref="AutomaticContainerInfo"/> tells the service to manage the container lifetime. Some services
/// always provision a fresh container in this mode, while others reuse a container associated with the current
/// conversation or message history. Adapter implementations should not rely on this option implying a brand-new
/// container.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class AutomaticContainerInfo : ContainerInfo
{
    /// <summary>Initializes a new instance of the <see cref="AutomaticContainerInfo"/> class.</summary>
    /// <param name="inputs">Content to make available to the hosted container.</param>
    public AutomaticContainerInfo(IList<AIContent>? inputs = null)
    {
        Inputs = inputs;
    }

    /// <summary>Gets or sets content to make available to the hosted container.</summary>
    /// <remarks>
    /// Services support varied input kinds. Most support IDs of files hosted by the service, represented via
    /// <see cref="HostedFileContent"/>. Some also support binary data, represented via <see cref="DataContent"/>.
    /// Unsupported inputs will be ignored by the <see cref="IChatClient"/> to which the tool is passed.
    /// Some services treat these inputs as additive to a container that already exists, rather than as the only
    /// inputs to a freshly created one.
    /// </remarks>
    public IList<AIContent>? Inputs { get; set; }
}
