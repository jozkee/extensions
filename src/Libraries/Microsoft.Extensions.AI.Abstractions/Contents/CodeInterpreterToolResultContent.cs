// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents the result of a code interpreter tool invocation by a hosted service.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AICodeInterpreter, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class CodeInterpreterToolResultContent : ToolResultContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodeInterpreterToolResultContent"/> class.
    /// </summary>
    /// <param name="callId">The tool call ID.</param>
    public CodeInterpreterToolResultContent(string callId)
        : base(callId)
    {
    }

    /// <summary>Gets or sets the ID of the hosted container used for the tool call.</summary>
    /// <remarks>
    /// The container ID can be supplied to <see cref="HostedCodeInterpreterTool.ContainerId"/> on a subsequent request
    /// to reuse files and other state retained by the hosted code execution environment.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or composed entirely of whitespace.</exception>
    public string? ContainerId
    {
        get;
        set => field = value is not null ? Throw.IfNullOrWhitespace(value) : value;
    }

    /// <summary>
    /// Gets or sets the output of code interpreter tool.
    /// </summary>
    /// <remarks>
    /// Outputs can include various types of content such as <see cref="HostedFileContent"/> for files,
    /// <see cref="DataContent"/> for binary data, <see cref="TextContent"/> for standard output text,
    /// or other <see cref="AIContent"/> types as supported by the service.
    /// </remarks>
    public IList<AIContent>? Outputs { get; set; }
}
