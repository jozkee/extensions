# [API Proposal]: Reuse hosted code interpreter containers

## Background and motivation

Hosted code execution tools can retain files and process state in provider-managed containers. OpenAI Responses code interpreter supports automatic containers and explicit reuse by passing a `cntr_...` container ID ([docs](https://developers.openai.com/api/docs/guides/tools-code-interpreter#containers)); OpenAI's hosted shell tool uses the same conceptual model via `container_auto` and `container_reference` ([docs](https://developers.openai.com/api/docs/guides/tools-shell#reuse-a-container-across-requests)). Anthropic code execution similarly accepts a request-level `container.id` and returns the used container on the response ([docs](https://platform.claude.com/docs/en/agents-and-tools/tool-use/code-execution-tool#containers)).

MEAI currently exposes `HostedCodeInterpreterTool` and code interpreter call/result content, but it does not expose the provider container ID. Users who want to reuse generated files, installed packages, or interpreter state must drop to provider-specific SDK types or inspect `RawRepresentation`, which is not portable and does not work consistently across streaming and non-streaming responses.

Empirical testing also showed that hosted containers are a per-request resource, not a per-tool resource: when both `code_interpreter` and `shell` tools are sent in the same OpenAI Responses request, the API returns HTTP 400 `mutually_exclusive_parameters` for every combination - same container ID, different container IDs, even `shell.environment=local` (script and run log: `D:\meai-stabilization\proposal1-prototype\code-execution-features\multi-tool-containers`). Anthropic models the same constraint by exposing `container` as a top-level property on `MessageCreateParams` shared by `code_execution` and `bash`. The right place for the container in MEAI is therefore `ChatOptions`, not a single tool instance.

Anthropic's programmatic tool-calling flow also makes container continuity important inside a single `FunctionInvokingChatClient` request. Code executing in a hosted container can pause, ask the client to invoke a local tool, and then resume only if the next model request targets the same container. Because `FunctionInvokingChatClient` already passes the prior assistant turns back to the inner client between iterations, adapters can lift the container ID from those messages whenever `ChatOptions.Container` is `null` - no MEAI middleware opt-in is required.

No related `dotnet/extensions` API proposal was found during issue search.

## API Proposal

```csharp
namespace Microsoft.Extensions.AI;

public abstract partial class ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public static ExistingContainerInfo FromExisting(string containerId);
}

public sealed partial class ExistingContainerInfo : ContainerInfo
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    public string ContainerId { get; set; }
}

public partial class ChatOptions
{
    [Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
    [JsonIgnore]
    public ContainerInfo? Container { get; set; }
}

[Experimental("MEAI001", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
public sealed partial class CodeInterpreterToolCallContent
{
    public string? ContainerId { get; set; }
}
```

`ChatOptions.Container == null` (the default) lets the `IChatClient` choose how to provision the container and additionally permits adapter-side implicit lifting: if the supplied chat history contains a prior `CodeInterpreterToolCallContent.ContainerId`, the adapter walks the history in reverse and reuses the most recent one. `ContainerInfo.FromExisting(id)` requests reuse of a specific hosted container when the provider supports it and takes precedence over any history-based lift. The `Container` property lives on `ChatOptions` so it is shared across all container-aware hosted tools in the request, matching how OpenAI and Anthropic actually validate the field server-side. It is `[JsonIgnore]`d because container IDs are runtime-only / transient; serializing them across persistence boundaries provides little value and conflicts with the experimental `ContainerInfo` polymorphism.

The existing stable `HostedCodeInterpreterTool.Inputs` property is unchanged and remains the way to seed `container_auto.file_ids` for OpenAI when the caller wants automatic provisioning with initial files. `CodeInterpreterToolCallContent.ContainerId` is the response-side surface that lets callers discover the container the service used, so they can pass it back in a later request via `ContainerInfo.FromExisting`.

Prototype artifacts:

| Adapter | Local artifact |
| --- | --- |
| `dotnet/extensions` | Branch `api-proposal/meai-container-options` (not pushed) at `D:\extensions-container-options` |
| OpenAI Responses | In-tree prototype in `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` |
| Anthropic | `D:\meai-proposal-scratch\container-reuse\anthropic-sdk-csharp`, branch `meai-proposal/container-reuse`, patch `D:\meai-proposal-scratch\container-reuse\anthropic-container-reuse.patch` |
| Google Vertex AI | `D:\meai-proposal-scratch\container-reuse\google-cloud-dotnet`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |
| Google Gemini / GenAI | `D:\meai-proposal-scratch\container-reuse\dotnet-genai`, branch `meai-proposal/container-reuse`, fit-check only: no reusable container ID in the code execution request/response shape |

## API Usage

Explicit container reuse across two requests:

```csharp
using Microsoft.Extensions.AI;

ChatResponse firstResponse = await chatClient.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Create a CSV file named results.csv with three rows.")
    ],
    new ChatOptions
    {
        Tools = [new HostedCodeInterpreterTool()],
    });

string? containerId =
    firstResponse.Messages
        .SelectMany(m => m.Contents)
        .OfType<CodeInterpreterToolCallContent>()
        .FirstOrDefault(c => c.ContainerId is not null)
        ?.ContainerId;

if (containerId is null)
{
    throw new InvalidOperationException("The provider did not return a reusable container ID.");
}

ChatResponse secondResponse = await chatClient.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Read results.csv and calculate the row count.")
    ],
    new ChatOptions
    {
        Container = ContainerInfo.FromExisting(containerId),
        Tools = [new HostedCodeInterpreterTool()],
    });
```

Programmatic tool-calling with implicit container reuse via the Anthropic adapter:

```csharp
using Microsoft.Extensions.AI;

IChatClient client = innerClient
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// First request: leave Container at its default (null). Adapters that support
// implicit lifting will reuse the prior container ID from history when present.
ChatResponse first = await client.GetResponseAsync(
    [
        new ChatMessage(ChatRole.User, "Use Python to load file.csv. Call get_metadata() if you need metadata."),
    ],
    new ChatOptions
    {
        Tools =
        [
            new HostedCodeInterpreterTool(),
            AIFunctionFactory.Create(GetMetadata, "get_metadata"),
        ],
    });

// Second request: the caller passes the prior assistant turns back in.
// Container is null, so the Anthropic adapter walks the supplied history in
// reverse and lifts the most recent CodeInterpreterToolCallContent.ContainerId
// from `first.Messages`.
List<ChatMessage> history = [.. first.Messages, new ChatMessage(ChatRole.User, "Plot a histogram of the loaded data.")];

ChatResponse second = await client.GetResponseAsync(
    history,
    new ChatOptions
    {
        Tools = [new HostedCodeInterpreterTool()],
    });
```

`Container = ContainerInfo.FromExisting(id)` overrides the implicit lift. `Container == null` is the default and permits adapter-side history lifting; if no prior `CodeInterpreterToolCallContent.ContainerId` is present in the supplied messages, the adapter sends no container hint and the provider applies its defaults.

## Alternative Designs

- Per-tool container slot on `HostedCodeInterpreterTool`. The original prototype put `Container` on the hosted tool. Empirical testing showed OpenAI Responses rejects every combination of `code_interpreter` + `shell` in the same request with HTTP 400 `mutually_exclusive_parameters` - same container ID, different container IDs, and even `shell.environment=local`. The container is a per-request resource, not a per-tool resource, so a per-tool API surface would let callers express something the provider cannot honor. Anthropic models the same constraint by exposing `container` at the top of `MessageCreateParams`, shared by `code_execution` and `bash`.
- Use `AdditionalProperties` or provider raw SDK types only. This keeps the core API smaller, but forces non-portable code and does not give MEAI consumers a consistent response-side place to discover the container ID.
- Add a new generic `HostedContainer` abstraction. This may be useful if MEAI later adds richer hosted container management APIs, but it is unnecessary for the current code interpreter reuse scenario.
- Put `ContainerId` directly on `ChatOptions` as a string. This is smaller, but it does not leave room for richer `ContainerInfo` shapes (e.g., a future opt-out marker or provider-specific configuration). The current `ContainerInfo` discriminated shape separates "reuse this exact container" (`ExistingContainerInfo`) from "let the service decide; adapters may lift from history" (`null`).
- A distinct `Automatic()`/`AutomaticContainerInfo` opt-in for history lifting. An earlier prototype required callers to opt in via `ContainerInfo.Automatic()` before the Anthropic adapter would lift a container ID from history. In practice the two states - "no container" and "automatic with optional lift" - behaved the same on every adapter validated: neither sets an explicit ID, both let the service decide, and the lift is a strict improvement over discarding free continuity. Collapsing them to "`null` is automatic; lift if history has it" removes one type, removes the `Automatic()` factory, and keeps the explicit-reuse path (`FromExisting`) unambiguous.
- Carry initial inputs on `AutomaticContainerInfo`. The earlier prototype had `AutomaticContainerInfo.Inputs`. OpenAI treats `container_auto.file_ids` as additive: combined with conversation continuity, files are added to whichever container the service selects rather than seeding a brand-new one. Carrying inputs on the container info also conflicted with the new `ChatOptions.Container` location, since file inputs are conceptually a tool-level concern. The proposal drops the type entirely and routes file inputs through the existing stable `HostedCodeInterpreterTool.Inputs`.
- Add an opt-in `FunctionInvokingChatClient.EnableCodeInterpreterContainerReuse` that copies the container ID into the next internal request. The earlier prototype tried this but found that providers like Anthropic already maintain container continuity when callers pass back the prior assistant messages - which `FunctionInvokingChatClient` already does. The middleware-level opt-in was redundant for those providers and fragile when combined with provider-specific reuse rules. The proposal instead lets adapters lift the container ID from chat history themselves when `Container` is `null`.
- Serialize `ChatOptions.Container` through `System.Text.Json`. The property is `[JsonIgnore]`d. Container IDs are runtime-only and ephemeral (OpenAI 20-minute idle expiry, Anthropic 30-day idle expiry); persisting them in serialized options leads to broken reuse attempts. Callers that need to restore an explicit container after deserialization can rebuild `ContainerInfo.FromExisting(id)` from their own storage.
- Rely on `HostedFileContent.Scope`. That captures generated file scope for OpenAI file outputs, but it does not identify the reusable execution environment or preserve interpreter state.
- Rely on provider automatic reuse through conversation context. This helps within a single provider-specific flow, but it does not let applications persist and explicitly reuse a known container across requests outside that conversation.

## Risks

- Container IDs are provider-specific and ephemeral. OpenAI code interpreter containers expire after 20 minutes of idle time; Anthropic code execution containers expire after 30 days. Applications must handle provider errors by creating a new container.
- "Automatic" (i.e., `Container == null`) does not mean "fresh". OpenAI's `container_auto` and Anthropic's auto-container behavior frequently associate the request with a container bound to the current conversation or supplied message history. Callers who need a guaranteed fresh container must rotate the conversation or otherwise sever continuity at the request level - the API surface does not enforce that.
- OpenAI's `container_auto.file_ids` (sourced from `HostedCodeInterpreterTool.Inputs`) is additive: file IDs are added to whichever container the service selects rather than seeding a clean container. Callers should not rely on `Inputs` to imply isolation.
- Implicit adapter lifting depends on the caller passing the prior assistant turns back in. If a host trims chat history (custom truncation, summarization, dropping tool-call messages), the adapter has nothing to lift and the request behaves as if no prior container existed. Adapters intentionally do not consult anything outside the supplied messages.
- Because the lift is implicit on `null` (the default), callers who pass partial history for context but do not want continuity can still get an unintended container reuse. To force a fresh container, callers must drop prior `CodeInterpreterToolCallContent` entries from the supplied history.
- The container is a per-request, single-slot resource on every provider validated so far. Callers who set `Container` and request multiple container-aware hosted tools in the same request (for example, `code_interpreter` + a future `shell` tool) will hit provider-side rejection rather than getting two independent containers. This is the main reason the property lives on `ChatOptions` rather than per tool.
- `ChatOptions.Container` is `[JsonIgnore]`d. It will not roundtrip through `JsonSerializer.Serialize(ChatOptions)`, so callers persisting options must rebuild the `ContainerInfo` from their own storage on the way out.
- Some providers expose code execution but not reusable container IDs. Those adapters should leave `ContainerId` null on the response and ignore request-side `Container` values unless the backend can express them.
- Streaming providers may deliver container metadata separately from code deltas. The prototype preserves the first non-null `ContainerId` observed during streaming for code interpreter call updates.
- `HostedCodeInterpreterTool.Inputs` is already stable, so the prototype keeps it as the single way to pass `container_auto.file_ids` to OpenAI. Adapters route those file IDs whenever `ChatOptions.Container` is `null`.
- OpenAI Responses remains an experimental OpenAI SDK surface (`OPENAI001`); this proposal adds MEAI experimental surface under the existing code interpreter diagnostic (`MEAI001`).

## Usage in Microsoft.Extensions.AI

### Updated in prototype

| File | Description |
| --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ContainerInfo.cs` | Adds the `FromExisting` factory; the abstract base type carries no `Automatic`/marker factory. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\ExistingContainerInfo.cs` | Adds request-side existing container ID with whitespace validation. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatOptions.cs` | Adds the experimental `Container` property (annotated `[JsonIgnore]`) and clones it in the protected copy ctor. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Tools\HostedCodeInterpreterTool.cs` | Updated remarks to point at `ChatOptions.Container`; the stable `Inputs` property is unchanged. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolCallContent.cs` | Adds response-side `ContainerId` for code interpreter calls. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Contents\CodeInterpreterToolResultContent.cs` | Removes the prototype result-side `ContainerId`; container IDs are call-content-only. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\ChatCompletion\ChatResponseExtensions.cs` | Coalesces streaming code interpreter call updates without losing `ContainerId`. |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIResponsesChatClient.cs` | Maps `ChatOptions.Container is ExistingContainerInfo` to OpenAI explicit container references; falls back to `HostedCodeInterpreterTool.Inputs` for `container_auto.file_ids`; captures `CodeInterpreterCallResponseItem.ContainerId` on streaming `output_item.added` and threads it through subsequent code-delta updates. |
| `<anthropic-sdk-csharp>\src\Anthropic\Services\Beta\Messages\AnthropicBetaClientExtensions.cs` | Resolves the Anthropic top-level container from `ChatOptions.Container`: `ExistingContainerInfo` maps to its ID; `null` triggers a reverse walk of the supplied chat history to lift the most recent `CodeInterpreterToolCallContent.ContainerId`. |
| `src\Libraries\Microsoft.Extensions.AI.Abstractions\Microsoft.Extensions.AI.Abstractions.json` | Updates the API baseline for the new experimental `ContainerInfo`/`ExistingContainerInfo`, call-content `ContainerId`, and `ChatOptions.Container`. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Tools\HostedCodeInterpreterToolTests.cs` | Covers default state, `Inputs` roundtrip, and `ContainerInfo.FromExisting` validation. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\ChatCompletion\ChatOptionsTests.cs` | Covers `ChatOptions.Container` default, roundtrip, and clone behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolCallContentTests.cs` | Covers call-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.Abstractions.Tests\Contents\CodeInterpreterToolResultContentTests.cs` | Covers result-content default, roundtrip, JSON serialization, and validation behavior. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientTests.cs` | Covers OpenAI explicit-container request serialization (`ChatOptions.Container`) plus non-streaming and streaming response mapping. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIConversionTests.cs` | Covers `HostedCodeInterpreterTool.AsOpenAIResponseTool()` for `Inputs`-backed automatic container configuration. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIHostedFileClientIntegrationTests.cs` | Replaces raw OpenAI response inspection with `CodeInterpreterToolCallContent.ContainerId`. |
| `<anthropic-sdk-csharp>\src\Anthropic.Tests\AnthropicClientBetaExtensionsTests.cs` | Tests for the Anthropic adapter container resolution: single-turn no-reuse, multi-turn implicit lift on `null`, and explicit `FromExisting` override. |

### Candidates or inapplicable sites

| File | Classification | Notes |
| --- | --- | --- |
| `src\Libraries\Microsoft.Extensions.AI.OpenAI\OpenAIAssistantsChatClient.cs` | Inapplicable | The Assistants adapter has its own code interpreter/thread model and does not expose the OpenAI Responses container reference shape used by this proposal. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIResponseClientIntegrationTests.cs` | Candidate | Live Responses integration tests can add an explicit container reuse scenario after API review; not required for the local prototype. |
| `test\Libraries\Microsoft.Extensions.AI.OpenAI.Tests\OpenAIAssistantChatClientIntegrationTests.cs` | Inapplicable | Exercises Assistants code interpreter behavior, not Responses containers. |
| `test\Libraries\Microsoft.Extensions.AI.Tests\ChatCompletion\OpenTelemetryChatClientTests.cs` | Inapplicable | Verifies telemetry around tool lists and does not inspect hosted container state. |

## Prototype validation

- ApiChief baseline regenerated for `Microsoft.Extensions.AI.Abstractions`. The diff against the previous prototype removes `AutomaticContainerInfo` and the `ContainerInfo.Automatic()` factory; it retains `ChatOptions.Container`, `ContainerInfo.FromExisting`, `ExistingContainerInfo`, and `CodeInterpreterToolCallContent.ContainerId`.
- ApiChief baseline for `Microsoft.Extensions.AI` is unchanged; the prototype no longer adds public surface to that assembly.
- `Microsoft.Extensions.AI.Abstractions` and `Microsoft.Extensions.AI.OpenAI` built for `net10.0` (`--no-dependencies`).
- Targeted tests passed:
  - `Microsoft.Extensions.AI.Abstractions.Tests` (`HostedCodeInterpreterToolTests`): 15 passed.
  - `Microsoft.Extensions.AI.Abstractions.Tests` (`ChatOptionsTests`): 7 passed.
  - `Microsoft.Extensions.AI.OpenAI.Tests` (`!~IntegrationTests`): 357 passed.
  - Anthropic scratch (`Anthropic.Tests` `AnthropicClientBetaExtensionsTests`): 215 passed on `net8.0`, 215 on `net472`.
- Multi-tool container experiment recorded at `D:\meai-stabilization\proposal1-prototype\code-execution-features\multi-tool-containers` shows OpenAI rejects every `code_interpreter` + `shell` combination with HTTP 400 `mutually_exclusive_parameters`, motivating the move from a per-tool `Container` to `ChatOptions.Container`.
- Focused multi-model review completed with `gpt-5.3-codex` and `claude-opus-4.7`. Empirical findings drove the redesign: "automatic" containers are not always fresh because providers tie continuity to the conversation/message history; `container_auto.file_ids` is additive rather than seeding a clean container; the container is a per-request, single-slot resource validated server-side; and an unconditional middleware-level container lift inside `FunctionInvokingChatClient` was redundant for providers that already maintain continuity through chat history. The proposal therefore promotes the container to `ChatOptions`, drops `AutomaticContainerInfo` entirely, and uses adapter-side implicit lifting triggered by the default `null` value (with `FromExisting` taking precedence).
