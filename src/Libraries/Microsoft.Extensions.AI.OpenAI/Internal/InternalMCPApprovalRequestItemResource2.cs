// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace OpenAI.Responses;

internal sealed class InternalMCPApprovalRequestItemResource2// : ResponseItem
{
    internal InternalMCPApprovalRequestItemResource2(string id, string serverLabel, string name, string arguments)
    {
        ServerLabel = serverLabel;
        Name = name;
        Arguments = arguments;
        Id = id;
    }

    public string ServerLabel { get; }

    public string Name { get; }

    public string Arguments { get; }

    public string Id { get; set; }
}
