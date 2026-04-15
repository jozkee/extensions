// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>
/// Represents a client-side tool search function that can be described to an AI service and invoked.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ToolSearchFunction"/> enables client-side tool search: the consumer provides a search delegate
/// that returns a list of <see cref="AITool"/> instances matching the search criteria. The adapter recognizes
/// the <see cref="ToolSearchFunction"/> subtype and handles the wire conversion per provider.
/// </para>
/// <para>
/// Auto-invoke works — the <see cref="AIFunction"/> invocation loop calls the delegate and feeds results back.
/// The delegate's return value (<see cref="IList{T}"/> of <see cref="AITool"/>) is converted by the adapter:
/// for OpenAI, tool definitions are sent as <c>tool_search_output.tools[]</c>; for Anthropic, tool references
/// are sent as <c>tool_result</c> with <c>tool_reference</c> blocks.
/// </para>
/// </remarks>
public class ToolSearchFunction : AIFunction
{
    /// <summary>The name of the tool search function.</summary>
    private readonly string _name;

    /// <summary>The description of the tool search function.</summary>
    private readonly string _description;

    /// <summary>The JSON schema for the function parameters.</summary>
    private readonly JsonElement _parametersSchema;

    /// <summary>The search delegate that returns tools matching the search criteria.</summary>
    private readonly Func<AIFunctionArguments, CancellationToken, ValueTask<IList<AITool>>> _searchFunc;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSearchFunction"/> class with an asynchronous search delegate.
    /// </summary>
    /// <param name="description">The description of the tool search function, suitable for use in describing the purpose to a model.</param>
    /// <param name="parametersSchema">A JSON schema describing the function's input parameters.</param>
    /// <param name="searchFunc">
    /// The asynchronous search delegate that returns tools matching the search criteria.
    /// </param>
    /// <param name="name">The name of the tool search function. Defaults to <c>"tool_search"</c> if not specified.</param>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="searchFunc"/> is <see langword="null"/>.</exception>
    public ToolSearchFunction(
        string description,
        JsonElement parametersSchema,
        Func<AIFunctionArguments, CancellationToken, ValueTask<IList<AITool>>> searchFunc,
        string? name = null)
    {
        _description = Throw.IfNull(description);
        _parametersSchema = parametersSchema;
        _searchFunc = Throw.IfNull(searchFunc);
        _name = name ?? "tool_search";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSearchFunction"/> class with a synchronous search delegate.
    /// </summary>
    /// <param name="description">The description of the tool search function, suitable for use in describing the purpose to a model.</param>
    /// <param name="parametersSchema">A JSON schema describing the function's input parameters.</param>
    /// <param name="searchFunc">
    /// The synchronous search delegate that returns tools matching the search criteria.
    /// </param>
    /// <param name="name">The name of the tool search function. Defaults to <c>"tool_search"</c> if not specified.</param>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="searchFunc"/> is <see langword="null"/>.</exception>
    public ToolSearchFunction(
        string description,
        JsonElement parametersSchema,
        Func<AIFunctionArguments, IList<AITool>> searchFunc,
        string? name = null)
    {
        _ = Throw.IfNull(searchFunc);
        _description = Throw.IfNull(description);
        _parametersSchema = parametersSchema;
        _searchFunc = (args, _) => new ValueTask<IList<AITool>>(searchFunc(args));
        _name = name ?? "tool_search";
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _parametersSchema;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken) =>
        await _searchFunc(arguments, cancellationToken).ConfigureAwait(false);
}
