// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Provides a decorator over an <see cref="AITool"/> that signals tool-search-related
/// metadata (such as deferred loading and namespace assignment) on a per-tool basis.
/// </summary>
/// <remarks>
/// <para>
/// When a <see cref="HostedToolSearchTool"/> is also present in the tools list, providers
/// that support tool search will honor the markers carried by this decorator. Markers on
/// the decorator take priority over the bulk configuration on
/// <see cref="HostedToolSearchTool.DeferredTools"/>.
/// </para>
/// <para>
/// All <see cref="AITool"/> members delegate to <see cref="InnerTool"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIToolSearch, UrlFormat = DiagnosticIds.UrlFormat)]
public class SearchableAITool : AITool
{
    /// <summary>Initializes a new instance of the <see cref="SearchableAITool"/> class.</summary>
    /// <param name="innerTool">The wrapped tool.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerTool"/> is <see langword="null"/>.</exception>
    public SearchableAITool(AITool innerTool)
    {
        InnerTool = Throw.IfNull(innerTool);
    }

    /// <summary>Gets the inner <see cref="AITool"/> being wrapped.</summary>
    public AITool InnerTool { get; }

    /// <summary>
    /// Gets or sets the namespace name under which this tool should be grouped when tool search is enabled.
    /// </summary>
    public string? Namespace { get; set; }

    /// <inheritdoc />
    public override string Name => InnerTool.Name;

    /// <inheritdoc />
    public override string Description => InnerTool.Description;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => InnerTool.AdditionalProperties;

    /// <inheritdoc />
    public override string ToString() => InnerTool.ToString();

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = Throw.IfNull(serviceType);

        return
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this :
            InnerTool.GetService(serviceType, serviceKey);
    }
}
