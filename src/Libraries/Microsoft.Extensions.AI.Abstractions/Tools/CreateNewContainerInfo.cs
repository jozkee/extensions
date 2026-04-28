// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.AI;

/// <summary>Describes a new hosted container to create for code interpreter tool calls.</summary>
[Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class CreateNewContainerInfo : ContainerInfo
{
    /// <summary>Initializes a new instance of the <see cref="CreateNewContainerInfo"/> class.</summary>
    /// <param name="inputs">Content to make available to the new hosted container.</param>
    public CreateNewContainerInfo(IList<AIContent>? inputs = null)
    {
        Inputs = inputs;
    }

    /// <summary>Gets or sets content to make available to the new hosted container.</summary>
    /// <remarks>
    /// Services support varied input kinds. Most support IDs of files hosted by the service, represented via
    /// <see cref="HostedFileContent"/>. Some also support binary data, represented via <see cref="DataContent"/>.
    /// Unsupported inputs will be ignored by the <see cref="IChatClient"/> to which the tool is passed.
    /// </remarks>
    public IList<AIContent>? Inputs { get; set; }
}
