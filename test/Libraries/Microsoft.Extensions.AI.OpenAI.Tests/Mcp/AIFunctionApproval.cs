// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.AI.Mcp;

public sealed class AIFunctionApproval : AIFunction
{
    private readonly AIFunction _inner;
    private readonly Func<object?, bool> _approveDelegate;

    public AIFunctionApproval(AIFunction inner, Func<object?, bool> approveDelegate)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _approveDelegate = approveDelegate ?? throw new ArgumentNullException(nameof(approveDelegate));
    }

    /// <inheritdoc/>
    public override string Name => _inner.Name;

    /// <inheritdoc/>
    public override string Description => _inner.Description;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => _inner.JsonSchema;

    /// <inheritdoc/>
    public override JsonElement? ReturnJsonSchema => _inner.ReturnJsonSchema;

    /// <inheritdoc/>
    public override JsonSerializerOptions JsonSerializerOptions => _inner.JsonSerializerOptions;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _inner.AdditionalProperties;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (_approveDelegate(this))
        {
            return _inner.InvokeAsync(arguments, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Function invocation not approved.");
        }
    }
}
