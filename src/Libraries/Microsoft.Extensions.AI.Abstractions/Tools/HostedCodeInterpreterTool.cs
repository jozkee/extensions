// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Represents a hosted tool that can be specified to an AI service to enable it to execute code it generates.</summary>
/// <remarks>
/// This tool does not itself implement code interpretation. It is a marker that can be used to inform a service
/// that the service is allowed to execute its generated code if the service is capable of doing so.
/// </remarks>
public class HostedCodeInterpreterTool : AITool
{
    /// <summary>Any additional properties associated with the tool.</summary>
    private IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>Initializes a new instance of the <see cref="HostedCodeInterpreterTool"/> class.</summary>
    public HostedCodeInterpreterTool()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HostedCodeInterpreterTool"/> class.</summary>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    public HostedCodeInterpreterTool(IReadOnlyDictionary<string, object?>? additionalProperties)
    {
        _additionalProperties = additionalProperties;
    }

    /// <summary>Creates a shallow clone of the current <see cref="HostedCodeInterpreterTool"/> instance.</summary>
    /// <returns>A shallow clone of the current <see cref="HostedCodeInterpreterTool"/> instance.</returns>
    /// <remarks>
    /// The clone will have the same values for all properties as the original instance. Any collections, like
    /// <see cref="Inputs"/> and <see cref="AdditionalProperties"/>, are shared with the original.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
    public virtual HostedCodeInterpreterTool Clone() => (HostedCodeInterpreterTool)MemberwiseClone();

    /// <inheritdoc />
    public override string Name => "code_interpreter";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties ?? base.AdditionalProperties;

    /// <summary>Gets or sets the ID of an existing hosted container to use for code interpreter tool calls.</summary>
    /// <remarks>
    /// When <see langword="null"/>, the service may create a new container or use its default container-selection behavior.
    /// When non-<see langword="null"/>, the service should use the referenced container if it is still available.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or composed entirely of whitespace.</exception>
    [Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
    public string? ContainerId
    {
        get;
        set => field = value is not null ? Throw.IfNullOrWhitespace(value) : value;
    }

    /// <summary>Gets or sets a collection of <see cref="AIContent"/> to be used as input to the code interpreter tool.</summary>
    /// <remarks>
    /// Services support different varied kinds of inputs. Most support the IDs of files that are hosted by the service,
    /// represented via <see cref="HostedFileContent"/>. Some also support binary data, represented via <see cref="DataContent"/>.
    /// Unsupported inputs will be ignored by the <see cref="IChatClient"/> to which the tool is passed.
    /// </remarks>
    public IList<AIContent>? Inputs { get; set; }
}
