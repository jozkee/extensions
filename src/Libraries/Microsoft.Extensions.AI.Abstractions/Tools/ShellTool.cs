// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.AI;

/// <summary>Represents a shell tool that can execute commands and be described to an AI service.</summary>
/// <remarks>
/// <para>
/// This class executes shell commands locally using <see cref="Process.Start(ProcessStartInfo)"/>.
/// Override <see cref="AIFunction.InvokeCoreAsync"/> to customize execution behavior.
/// </para>
/// <para>
/// <see cref="IChatClient"/> implementations backed by a service that has its own notion of a shell tool
/// can special-case this type, translating it into usage of the service's native shell tool.
/// For <see cref="IChatClient"/> implementations without such special-casing, the tool functions as
/// a standard <see cref="AIFunction"/> that can be invoked via <see cref="AIFunction.InvokeAsync"/>.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AIShell, UrlFormat = DiagnosticIds.UrlFormat)]
public class ShellTool : AIFunction
{
    /// <summary>The JSON schema defining the parameters for the shell tool.</summary>
    private static readonly JsonElement _jsonSchema = JsonElement.Parse("""
        {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "The shell command to execute."
                },
                "timeout_ms": {
                    "type": "integer",
                    "description": "Maximum execution time in milliseconds. Defaults to 120000."
                }
            },
            "required": ["command"]
        }
        """u8);

    /// <summary>Any additional properties associated with the tool.</summary>
    private IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>Initializes a new instance of the <see cref="ShellTool"/> class.</summary>
    public ShellTool()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ShellTool"/> class.</summary>
    /// <param name="additionalProperties">Any additional properties associated with the tool.</param>
    public ShellTool(IReadOnlyDictionary<string, object?>? additionalProperties)
    {
        _additionalProperties = additionalProperties;
    }

    /// <inheritdoc />
    public override string Name => "local_shell";

    /// <inheritdoc />
    public override string Description => "Executes a shell command and returns stdout, stderr, and exit code.";

    /// <inheritdoc />
    public override JsonElement JsonSchema => _jsonSchema;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties ?? base.AdditionalProperties;

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        _ = Throw.IfNull(arguments);

        string? command = arguments.TryGetValue("command", out object? cmdObj) ? cmdObj?.ToString() : null;

        bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        string shell = isWindows ? "cmd.exe" : "/bin/sh";
#pragma warning disable CA1307 // Specify StringComparison for clarity - Replace(string, string) is the only overload available on all TFMs
        string shellArgs = isWindows ? $"/c {command}" : $"-c \"{command?.Replace("\"", "\\\"")}\"";
#pragma warning restore CA1307

        int timeoutMs = 120_000;
        if (arguments.TryGetValue("timeout_ms", out object? timeoutObj))
        {
            if (timeoutObj is int t)
            {
                timeoutMs = t;
            }
            else if (int.TryParse(timeoutObj?.ToString(), out int parsed))
            {
                timeoutMs = parsed;
            }
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = shellArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var output = new ShellCommandOutput();

        try
        {
            _ = process.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

#pragma warning disable CA2016 // Forward the CancellationToken - ReadToEndAsync(CancellationToken) not available on all TFMs
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
#pragma warning restore CA2016

            // Wait for process to exit with timeout support.
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _ = tcs.TrySetResult(true);

            if (process.HasExited)
            {
                _ = tcs.TrySetResult(true);
            }

            using (cts.Token.Register(() => _ = tcs.TrySetCanceled(cts.Token)))
            {
                _ = await tcs.Task.ConfigureAwait(false);
            }

            output.Stdout = await stdoutTask.ConfigureAwait(false);
            output.Stderr = await stderrTask.ConfigureAwait(false);
            output.ExitCode = process.ExitCode;
#pragma warning disable S106 // Standard outputs should not be used directly to log anything
            Console.WriteLine($"Command executed with exit code {output.ExitCode}");
            Console.WriteLine($"Stdout: {output.Stdout}");
            Console.WriteLine($"Stderr: {output.Stderr}");
#pragma warning restore S106 // Standard outputs should not be used directly to log anything
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred, not user cancellation.
            output.TimedOut = true;

            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process may have already exited.
            }
        }

        return $"Exit Code: {output.ExitCode}\nStdout: {output.Stdout}\nStderr: {output.Stderr}";
    }
}
