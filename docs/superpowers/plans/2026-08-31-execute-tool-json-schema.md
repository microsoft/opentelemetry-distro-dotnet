# Execute Tool JSON Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add dictionary-backed concrete models for execute-tool argument and result JSON, and use them in `ExecuteToolScope` and the execute-tool ETW logger without breaking existing APIs.

**Architecture:** Public schema models inherit `Dictionary<string, object?>`, with typed convenience properties writing directly to standard JSON keys and inherited indexer writes using last-write-wins semantics. A dedicated internal serializer normalizes customer values into a JSON-safe graph and is shared by the span and ETW paths.

**Tech Stack:** C# latest, .NET Standard 2.0/.NET 8.0 library targets, `System.Text.Json`, MSTest, FluentAssertions, Microsoft PublicApiAnalyzers.

## Global Constraints

- Limit behavior changes to `ExecuteToolScope`, `ExecuteToolDataBuilder`, and execute-tool ETW logging.
- Preserve every shipped string and dictionary API and its current behavior.
- Every schema model must support arbitrary key/value pairs through its inherited dictionary.
- Standard property and indexer writes use last-write-wins semantics.
- All schema fields are optional; `ExecuteToolCallArguments` initializes `schema_version` to `"1.0"`.
- Customer payload content must not cause telemetry recording to throw.
- An unserializable dictionary value or collection element falls back to `ToString()`, then its full type name.
- Add no package dependency.

---

## File Structure

- Create `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ToolCallDictionaryAccessor.cs`: shared typed getter/setter behavior for dictionary-backed models.
- Create `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallArguments.cs`: arguments model, resource/identifier/container models, and action enum.
- Create `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallResult.cs`: result model, result-only nested models, and result enums.
- Create `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/ExecuteToolPayloadSerializer.cs`: non-throwing recursive normalization and compact JSON serialization.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolCallDetails.cs`: typed arguments constructor/property and precedence state.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`: typed argument serialization and typed result overload.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs`: typed result build path shared with ETW.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/IA365EtwLogger.cs`: unambiguous typed result overload.
- Modify `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/A365EtwLogger.cs`: typed result implementation.
- Create `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ExecuteToolJsonModelsTests.cs`: dictionary-model behavior and complete schema examples.
- Create `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/ExecuteToolPayloadSerializerTests.cs`: safe normalization and fallback behavior.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs`: typed span arguments/results and compatibility.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs`: typed builder output and precedence.
- Modify `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwLoggingBuilderTests.cs`: typed ETW event payload.
- Modify `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`: new public types, members, and overloads.
- Modify `docs/agent365-getting-started.md`: typed execute-tool examples and custom-field guidance.

---

### Task 1: Add Dictionary-Backed Schema Models

**Files:**
- Create: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ToolCallDictionaryAccessor.cs`
- Create: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallArguments.cs`
- Create: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/Tools/ExecuteToolCallResult.cs`
- Create: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Contracts/ExecuteToolJsonModelsTests.cs`

**Interfaces:**
- Produces: `ExecuteToolCallArguments`, `ToolCallResource`, `ToolCallIdentifier`, `ToolCallContainer`, `ExecuteToolCallResult`, `ToolCallResultResource`, `ToolCallResultOutcome`, `ToolCallResultSensitivity`, `ToolCallResultPolicy`, `ToolCallResultSecurity`, `ToolCallResultPagination`.
- Produces: nullable enums `ToolCallAction`, `ToolCallOutcomeStatus`, and `ToolPolicyDecision` through typed properties.
- Consumes: only `System.Collections.Generic` and `System.Globalization`.

- [ ] **Step 1: Write failing model tests**

Create `ExecuteToolJsonModelsTests.cs` with tests that exercise dictionary storage rather than JSON serialization:

```csharp
namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing.Contracts;

using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

[TestClass]
public sealed class ExecuteToolJsonModelsTests
{
    [TestMethod]
    public void Arguments_DefaultsSchemaVersionAndStoresStandardProperties()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?> { ["format"] = "text" },
            Resources = new List<ToolCallResource>(),
        };

        arguments["schema_version"].Should().Be("1.0");
        arguments["action"].Should().Be("read");
        arguments.Action.Should().Be(ToolCallAction.Read);
        arguments.Parameters!["format"].Should().Be("text");
    }

    [TestMethod]
    public void StandardAndIndexerWritesUseLastWriteWins()
    {
        var policy = new ToolCallResultPolicy
        {
            Decision = ToolPolicyDecision.Allow,
        };

        policy["decision"] = "provider_conditional_allow";

        policy["decision"].Should().Be("provider_conditional_allow");
        policy.Decision.Should().BeNull();

        policy.Decision = ToolPolicyDecision.Deny;

        policy["decision"].Should().Be("deny");
        policy.Decision.Should().Be(ToolPolicyDecision.Deny);
    }

    [TestMethod]
    public void NullPropertyRemovesStandardKey()
    {
        var identifier = new ToolCallIdentifier
        {
            Type = "microsoft.graph.drive_item_id",
            Value = "01ABCDEF",
        };

        identifier.Type = null;

        identifier.Should().NotContainKey("type");
        identifier.Should().ContainKey("value");
    }

    [TestMethod]
    public void IdentifierAndContainerAcceptCustomProperties()
    {
        var identifier = new ToolCallIdentifier
        {
            ["provider_scope"] = "tenant",
        };
        var container = new ToolCallContainer
        {
            ["provider_path"] = "/sites/Engineering",
        };

        identifier["provider_scope"].Should().Be("tenant");
        container["provider_path"].Should().Be("/sites/Engineering");
    }

    [TestMethod]
    public void CopyConstructorPreservesCustomValues()
    {
        var result = new ExecuteToolCallResult(
            new Dictionary<string, object?>
            {
                ["provider_result"] = 42,
            });

        result["provider_result"].Should().Be(42);
    }
}
```

- [ ] **Step 2: Run model tests and verify they fail**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~ExecuteToolJsonModelsTests --no-restore
```

Expected: compilation fails because the `Contracts.Tools` namespace and model types do not exist.

- [ ] **Step 3: Implement the internal dictionary accessor**

Create `ToolCallDictionaryAccessor.cs`:

```csharp
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    internal static class ToolCallDictionaryAccessor
    {
        internal static T? GetReference<T>(IDictionary<string, object?> values, string key)
            where T : class
        {
            return values.TryGetValue(key, out var value) ? value as T : null;
        }

        internal static T? GetValue<T>(IDictionary<string, object?> values, string key)
            where T : struct
        {
            return values.TryGetValue(key, out var value) && value is T typed
                ? typed
                : null;
        }

        internal static TEnum? GetEnum<TEnum>(IDictionary<string, object?> values, string key)
            where TEnum : struct
        {
            if (!values.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value is TEnum typed)
            {
                return typed;
            }

            return value is string text &&
                Enum.TryParse<TEnum>(text, true, out var parsed)
                    ? parsed
                    : null;
        }

        internal static void SetReference<T>(IDictionary<string, object?> values, string key, T? value)
            where T : class
        {
            if (value == null)
            {
                values.Remove(key);
                return;
            }

            values[key] = value;
        }

        internal static void SetValue<T>(IDictionary<string, object?> values, string key, T? value)
            where T : struct
        {
            if (!value.HasValue)
            {
                values.Remove(key);
                return;
            }

            values[key] = value.Value;
        }

        internal static void SetEnum<TEnum>(IDictionary<string, object?> values, string key, TEnum? value)
            where TEnum : struct
        {
            if (!value.HasValue)
            {
                values.Remove(key);
                return;
            }

            values[key] = value.Value.ToString()!.ToLower(CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 4: Implement the arguments models**

Create `ExecuteToolCallArguments.cs`. Add XML documentation to every public type, constructor, enum value, and property so `GenerateDocumentationFile` and analyzers remain clean.

Use these exact public declarations and key mappings:

```csharp
namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    public enum ToolCallAction
    {
        Create,
        Read,
        Update,
        Delete,
    }

    public sealed class ExecuteToolCallArguments : Dictionary<string, object?>
    {
        public ExecuteToolCallArguments()
        {
            SchemaVersion = "1.0";
        }

        public ExecuteToolCallArguments(IDictionary<string, object?> values)
            : base(values ?? throw new ArgumentNullException(nameof(values)))
        {
        }

        public string? SchemaVersion
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "schema_version");
            set => ToolCallDictionaryAccessor.SetReference(this, "schema_version", value);
        }

        public IList<ToolCallResource>? Resources
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallResource>>(this, "resources");
            set => ToolCallDictionaryAccessor.SetReference(this, "resources", value);
        }

        public ToolCallAction? Action
        {
            get => ToolCallDictionaryAccessor.GetEnum<ToolCallAction>(this, "action");
            set => ToolCallDictionaryAccessor.SetEnum(this, "action", value);
        }

        public IDictionary<string, object?>? Parameters
        {
            get => ToolCallDictionaryAccessor.GetReference<IDictionary<string, object?>>(this, "parameters");
            set => ToolCallDictionaryAccessor.SetReference(this, "parameters", value);
        }
    }

    public sealed class ToolCallResource : Dictionary<string, object?>
    {
        public ToolCallResource() { }
        public ToolCallResource(IDictionary<string, object?> values)
            : base(values ?? throw new ArgumentNullException(nameof(values))) { }

        public string? Id
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "id");
            set => ToolCallDictionaryAccessor.SetReference(this, "id", value);
        }

        public string? Uri
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "uri");
            set => ToolCallDictionaryAccessor.SetReference(this, "uri", value);
        }

        public string? Name
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "name");
            set => ToolCallDictionaryAccessor.SetReference(this, "name", value);
        }

        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }

        public string? Provider
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "provider");
            set => ToolCallDictionaryAccessor.SetReference(this, "provider", value);
        }

        public IList<ToolCallIdentifier>? Identifiers
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallIdentifier>>(this, "identifiers");
            set => ToolCallDictionaryAccessor.SetReference(this, "identifiers", value);
        }

        public ToolCallContainer? Container
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallContainer>(this, "container");
            set => ToolCallDictionaryAccessor.SetReference(this, "container", value);
        }
    }

    public sealed class ToolCallIdentifier : Dictionary<string, object?>
    {
        public ToolCallIdentifier() { }
        public ToolCallIdentifier(IDictionary<string, object?> values)
            : base(values ?? throw new ArgumentNullException(nameof(values))) { }

        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }

        public string? Value
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "value");
            set => ToolCallDictionaryAccessor.SetReference(this, "value", value);
        }
    }

    public sealed class ToolCallContainer : Dictionary<string, object?>
    {
        public ToolCallContainer() { }
        public ToolCallContainer(IDictionary<string, object?> values)
            : base(values ?? throw new ArgumentNullException(nameof(values))) { }

        public string? Id
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "id");
            set => ToolCallDictionaryAccessor.SetReference(this, "id", value);
        }

        public string? Uri
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "uri");
            set => ToolCallDictionaryAccessor.SetReference(this, "uri", value);
        }

        public string? Type
        {
            get => ToolCallDictionaryAccessor.GetReference<string>(this, "type");
            set => ToolCallDictionaryAccessor.SetReference(this, "type", value);
        }
    }
}
```

- [ ] **Step 5: Implement the result models**

Create `ExecuteToolCallResult.cs` with XML documentation on all public APIs and these exact mappings:

```csharp
namespace Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools
{
    public enum ToolCallOutcomeStatus
    {
        Success,
        Failure,
    }

    public enum ToolPolicyDecision
    {
        Allow,
        Deny,
    }

    public sealed class ExecuteToolCallResult : Dictionary<string, object?>
    {
        public ExecuteToolCallResult() { }
        public ExecuteToolCallResult(IDictionary<string, object?> values)
            : base(values ?? throw new ArgumentNullException(nameof(values))) { }

        public ToolCallResultOutcome? Outcome
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultOutcome>(this, "outcome");
            set => ToolCallDictionaryAccessor.SetReference(this, "outcome", value);
        }

        public IList<ToolCallResultResource>? Resources
        {
            get => ToolCallDictionaryAccessor.GetReference<IList<ToolCallResultResource>>(this, "resources");
            set => ToolCallDictionaryAccessor.SetReference(this, "resources", value);
        }

        public IDictionary<string, object?>? Data
        {
            get => ToolCallDictionaryAccessor.GetReference<IDictionary<string, object?>>(this, "data");
            set => ToolCallDictionaryAccessor.SetReference(this, "data", value);
        }

        public ToolCallResultPagination? Pagination
        {
            get => ToolCallDictionaryAccessor.GetReference<ToolCallResultPagination>(this, "pagination");
            set => ToolCallDictionaryAccessor.SetReference(this, "pagination", value);
        }
    }
}
```

In the same file, add:

| Type | Standard properties and JSON keys |
| --- | --- |
| `ToolCallResultResource` | `Id`/`id`, `Uri`/`uri`, `Name`/`name`, `Type`/`type`, `Provider`/`provider`, `Identifiers`/`identifiers`, `Container`/`container`, `Outcome`/`outcome`, `Sensitivity`/`sensitivity`, `Policy`/`policy`, `Security`/`security`, `Data`/`data` |
| `ToolCallResultOutcome` | `Status`/`status` via `GetEnum`/`SetEnum`, `Code`/`code`, `ProviderCode`/`provider_code`, `Message`/`message` |
| `ToolCallResultSensitivity` | `LabelId`/`label_id` |
| `ToolCallResultPolicy` | `Decision`/`decision` via `GetEnum`/`SetEnum`, `Id`/`id`, `Name`/`name` |
| `ToolCallResultSecurity` | `XpiaDetected`/`xpia_detected` via `GetValue<bool>`/`SetValue` |
| `ToolCallResultPagination` | `HasMore`/`has_more` via `GetValue<bool>`/`SetValue`, `NextCursor`/`next_cursor`, `TotalCount`/`total_count` via `GetValue<long>`/`SetValue` |

Each type must have the same two constructors used by `ToolCallResource` and must inherit `Dictionary<string, object?>` directly.

- [ ] **Step 6: Run model tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~ExecuteToolJsonModelsTests
```

Expected: all `ExecuteToolJsonModelsTests` pass.

- [ ] **Step 7: Commit the schema models**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Contracts\Tools test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Contracts\ExecuteToolJsonModelsTests.cs
git commit -m "Add execute tool JSON schema models" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 9b834f8f-58fc-46f9-b92a-35a5c4890e2e"
```

---

### Task 2: Add Safe Execute-Tool Payload Serialization

**Files:**
- Create: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/ExecuteToolPayloadSerializer.cs`
- Create: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/ExecuteToolPayloadSerializerTests.cs`

**Interfaces:**
- Consumes: `IDictionary<string, object?>`.
- Produces: `internal static string ExecuteToolPayloadSerializer.Serialize(IDictionary<string, object?> payload)`.
- Guarantees: compact valid JSON and per-value fallback without customer-content exceptions.

- [ ] **Step 1: Write failing serializer tests**

Create tests for a full payload, custom values, one failing value, a failing collection element, and a cycle:

```csharp
namespace Microsoft.Agents.A365.Observability.Runtime.Tests.Tracing;

using System.Collections;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.A365.Observability.Runtime.Tracing;
using Microsoft.Agents.A365.Observability.Runtime.Tracing.Contracts.Tools;

[TestClass]
public sealed class ExecuteToolPayloadSerializerTests
{
    [TestMethod]
    public void Serialize_ProducesExpectedArgumentsJson()
    {
        var arguments = new ExecuteToolCallArguments
        {
            Action = ToolCallAction.Read,
            Parameters = new Dictionary<string, object?>
            {
                ["format"] = "text",
                ["includeMetadata"] = true,
            },
            Resources = new List<ToolCallResource>
            {
                new()
                {
                    Id = "canonical-resource-id",
                    Uri = "https://example.com/resource",
                    Name = "resource-name",
                    Type = "document",
                    Provider = "provider-name",
                    Identifiers = new List<ToolCallIdentifier>
                    {
                        new()
                        {
                            Type = "provider.identifier_type",
                            Value = "provider-specific-id",
                        },
                    },
                    Container = new ToolCallContainer
                    {
                        Id = "container-id",
                        Uri = "https://example.com/container",
                        Type = "site",
                    },
                },
            },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(arguments));
        var root = document.RootElement;

        root.GetProperty("schema_version").GetString().Should().Be("1.0");
        root.GetProperty("action").GetString().Should().Be("read");
        root.GetProperty("parameters").GetProperty("includeMetadata").GetBoolean().Should().BeTrue();
        root.GetProperty("resources")[0].GetProperty("identifiers")[0]
            .GetProperty("value").GetString().Should().Be("provider-specific-id");
    }

    [TestMethod]
    public void Serialize_ProducesExpectedResultJson()
    {
        var result = new ExecuteToolCallResult
        {
            Outcome = new ToolCallResultOutcome
            {
                Status = ToolCallOutcomeStatus.Success,
                Code = null,
                ProviderCode = null,
                Message = null,
            },
            Resources = new List<ToolCallResultResource>
            {
                new()
                {
                    Id = "canonical-resource-id",
                    Uri = "https://example.com/resource",
                    Name = "resource-name",
                    Type = "document",
                    Provider = "provider-name",
                    Identifiers = new List<ToolCallIdentifier>
                    {
                        new()
                        {
                            Type = "provider.identifier_type",
                            Value = "provider-specific-id",
                        },
                    },
                    Container = new ToolCallContainer
                    {
                        Id = "container-id",
                        Uri = "https://example.com/container",
                        Type = "site",
                    },
                    Outcome = new ToolCallResultOutcome
                    {
                        Status = ToolCallOutcomeStatus.Success,
                    },
                    Sensitivity = new ToolCallResultSensitivity
                    {
                        LabelId = "label-id",
                    },
                    Policy = new ToolCallResultPolicy
                    {
                        Decision = ToolPolicyDecision.Allow,
                        Id = "policy-id",
                        Name = "policy-name",
                    },
                    Security = new ToolCallResultSecurity
                    {
                        XpiaDetected = false,
                    },
                    Data = new Dictionary<string, object?>(),
                },
            },
            Data = new Dictionary<string, object?>(),
            Pagination = new ToolCallResultPagination
            {
                HasMore = false,
                NextCursor = null,
                TotalCount = 1,
            },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(result));
        var resource = document.RootElement.GetProperty("resources")[0];

        document.RootElement.GetProperty("outcome")
            .GetProperty("status").GetString().Should().Be("success");
        resource.GetProperty("sensitivity")
            .GetProperty("label_id").GetString().Should().Be("label-id");
        resource.GetProperty("policy")
            .GetProperty("decision").GetString().Should().Be("allow");
        resource.GetProperty("security")
            .GetProperty("xpia_detected").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("pagination")
            .GetProperty("total_count").GetInt64().Should().Be(1);
    }

    [TestMethod]
    public void Serialize_ReplacesOnlyFailingDictionaryValue()
    {
        var payload = new ExecuteToolCallResult
        {
            ["good"] = 42,
            ["bad"] = new ThrowingEnumerable(),
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));

        document.RootElement.GetProperty("good").GetInt32().Should().Be(42);
        document.RootElement.GetProperty("bad").GetString().Should().Be(nameof(ThrowingEnumerable));
    }

    [TestMethod]
    public void Serialize_ReplacesCycleWithoutThrowing()
    {
        var payload = new ExecuteToolCallResult();
        payload["self"] = payload;

        var action = () => ExecuteToolPayloadSerializer.Serialize(payload);

        action.Should().NotThrow();
        using var document = JsonDocument.Parse(action());
        document.RootElement.GetProperty("self").GetString()
            .Should().Contain(nameof(ExecuteToolCallResult));
    }

    [TestMethod]
    public void Serialize_ReplacesOnlyFailingCollectionElement()
    {
        var cyclic = new SelfReferencingValue();
        cyclic.Self = cyclic;
        var payload = new ExecuteToolCallResult
        {
            ["items"] = new object?[] { "first", cyclic, "last" },
        };

        using var document = JsonDocument.Parse(ExecuteToolPayloadSerializer.Serialize(payload));
        var items = document.RootElement.GetProperty("items");

        items[0].GetString().Should().Be("first");
        items[1].GetString().Should().Contain(nameof(SelfReferencingValue));
        items[2].GetString().Should().Be("last");
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("test");
        public override string ToString() => nameof(ThrowingEnumerable);
    }

    private sealed class SelfReferencingValue
    {
        public SelfReferencingValue? Self { get; set; }
        public override string ToString() => nameof(SelfReferencingValue);
    }
}
```

- [ ] **Step 2: Run serializer tests and verify they fail**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~ExecuteToolPayloadSerializerTests --no-restore
```

Expected: compilation fails because `ExecuteToolPayloadSerializer` does not exist.

- [ ] **Step 3: Implement recursive normalization**

Create `ExecuteToolPayloadSerializer.cs` with:

```csharp
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Microsoft.Agents.A365.Observability.Runtime.Tracing
{
    internal static class ExecuteToolPayloadSerializer
    {
        private const int MaximumDepth = 64;

        public static string Serialize(IDictionary<string, object?> payload)
        {
            if (payload == null)
            {
                return "{}";
            }

            var active = new HashSet<object>(ReferenceComparer.Instance);
            var normalized = NormalizeDictionary(payload, active, 0);
            return JsonSerializer.Serialize(normalized);
        }

        private static Dictionary<string, object?> NormalizeDictionary(
            IDictionary<string, object?> source,
            HashSet<object> active,
            int depth)
        {
            if (depth >= MaximumDepth || !active.Add(source))
            {
                return new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(source),
                };
            }

            try
            {
                var result = new Dictionary<string, object?>();
                foreach (var pair in source)
                {
                    result[pair.Key] = NormalizeValue(pair.Value, active, depth + 1);
                }

                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, object?>
                {
                    ["value"] = GetFallback(source),
                };
            }
            finally
            {
                active.Remove(source);
            }
        }

        private static object? NormalizeValue(object? value, HashSet<object> active, int depth)
        {
            if (value == null || value is string || value is bool ||
                value is byte || value is sbyte || value is short ||
                value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float ||
                value is double || value is decimal)
            {
                return value;
            }

            if (depth >= MaximumDepth || active.Contains(value))
            {
                return GetFallback(value);
            }

            if (value is IDictionary<string, object?> dictionary)
            {
                return NormalizeDictionary(dictionary, active, depth);
            }

            if (value is IEnumerable enumerable)
            {
                if (!active.Add(value))
                {
                    return GetFallback(value);
                }

                try
                {
                    var items = new List<object?>();
                    foreach (var item in enumerable)
                    {
                        items.Add(NormalizeValue(item, active, depth + 1));
                    }

                    return items;
                }
                catch (Exception)
                {
                    return GetFallback(value);
                }
                finally
                {
                    active.Remove(value);
                }
            }

            try
            {
                using var document = JsonDocument.Parse(
                    JsonSerializer.Serialize(value, value.GetType()));
                return document.RootElement.Clone();
            }
            catch (Exception)
            {
                return GetFallback(value);
            }
        }

        private static string GetFallback(object value)
        {
            try
            {
                return value.ToString() ?? value.GetType().FullName ?? value.GetType().Name;
            }
            catch (Exception)
            {
                return value.GetType().FullName ?? value.GetType().Name;
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
```

During implementation, keep the catch boundaries around customer-controlled
enumeration, object serialization, and `ToString()`. Do not catch around
unrelated span or ETW operations.

- [ ] **Step 4: Run serializer and model tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolPayloadSerializerTests|FullyQualifiedName~ExecuteToolJsonModelsTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit the serializer**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\ExecuteToolPayloadSerializer.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\ExecuteToolPayloadSerializerTests.cs
git commit -m "Add safe execute tool payload serialization" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 9b834f8f-58fc-46f9-b92a-35a5c4890e2e"
```

---

### Task 3: Integrate Typed Payloads with ExecuteToolScope

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Contracts/ToolCallDetails.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Tracing/Scopes/ExecuteToolScope.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Tracing/Scopes/ExecuteToolScopeTest.cs`

**Interfaces:**
- Consumes: `ExecuteToolCallArguments`, `ExecuteToolCallResult`, `ExecuteToolPayloadSerializer.Serialize`.
- Produces: `ToolCallDetails.ToolCallArguments`.
- Produces: `ExecuteToolScope.RecordResponse(ExecuteToolCallResult result)`.

- [ ] **Step 1: Write failing scope tests**

Add:

```csharp
[TestMethod]
public void Start_WithTypedArguments_RecordsSchemaJson()
{
    var arguments = new ExecuteToolCallArguments
    {
        Action = ToolCallAction.Read,
        Parameters = new Dictionary<string, object?> { ["location"] = "Seattle" },
        Resources = new List<ToolCallResource>(),
    };

    var activity = ListenForActivity(() =>
    {
        using var scope = ExecuteToolScope.Start(
            Util.GetDefaultRequest(),
            new ToolCallDetails("get_weather", arguments),
            Util.GetAgentDetails());
    });

    var json = activity.Tags.Single(
        pair => pair.Key == OpenTelemetryConstants.GenAiToolArgumentsKey).Value;
    using var document = JsonDocument.Parse(json!);
    document.RootElement.GetProperty("action").GetString().Should().Be("read");
    document.RootElement.GetProperty("schema_version").GetString().Should().Be("1.0");
}

[TestMethod]
public void RecordResponse_WithTypedResult_RecordsSchemaJson()
{
    var result = new ExecuteToolCallResult
    {
        Outcome = new ToolCallResultOutcome
        {
            Status = ToolCallOutcomeStatus.Success,
        },
        Data = new Dictionary<string, object?>(),
        Resources = new List<ToolCallResultResource>(),
        Pagination = new ToolCallResultPagination
        {
            HasMore = false,
            TotalCount = 0,
        },
    };

    var activity = ListenForActivity(() =>
    {
        using var scope = ExecuteToolScope.Start(
            Util.GetDefaultRequest(),
            new ToolCallDetails("get_weather", (string?)null),
            Util.GetAgentDetails());
        scope.RecordResponse(result);
    });

    var json = activity.Tags.Single(
        pair => pair.Key == OpenTelemetryConstants.GenAiToolCallResultKey).Value;
    using var document = JsonDocument.Parse(json!);
    document.RootElement.GetProperty("outcome")
        .GetProperty("status").GetString().Should().Be("success");
}
```

Add the `System.Text.Json` and `Contracts.Tools` using directives.

- [ ] **Step 2: Run scope tests and verify they fail**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter FullyQualifiedName~ExecuteToolScopeTest
```

Expected: compilation fails because the typed constructor/property and result overload do not exist.

- [ ] **Step 3: Extend ToolCallDetails**

Add:

```csharp
public ToolCallDetails(
    string toolName,
    ExecuteToolCallArguments toolCallArguments,
    string? toolCallId = null,
    string? description = null,
    string? toolType = null,
    Uri? endpoint = null)
{
    ToolName = toolName;
    ToolCallArguments = toolCallArguments ?? throw new ArgumentNullException(nameof(toolCallArguments));
    ToolCallId = toolCallId;
    Description = description;
    ToolType = toolType;
    Endpoint = endpoint;
}

public ExecuteToolCallArguments? ToolCallArguments { get; }
```

Add `Contracts.Tools` to the usings. Preserve the existing six-value
`Deconstruct` signature. Extend equality and hashing:

```csharp
ReferenceEquals(ToolCallArguments, other.ToolCallArguments)
```

and:

```csharp
hash = (hash * 31) + (ToolCallArguments != null
    ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ToolCallArguments)
    : 0);
```

- [ ] **Step 4: Add typed arguments precedence and typed result recording**

In `ExecuteToolScope`, select typed arguments before existing branches:

```csharp
if (details.ToolCallArguments != null)
{
    SetTagMaybe(
        OpenTelemetryConstants.GenAiToolArgumentsKey,
        ExecuteToolPayloadSerializer.Serialize(details.ToolCallArguments));
}
else if (details.ArgumentsObject != null)
{
    SetTagMaybe(
        OpenTelemetryConstants.GenAiToolArgumentsKey,
        MessageUtils.Serialize(details.ArgumentsObject));
}
else if (arguments != null)
{
    // Keep the existing JSON detection and wrapping body unchanged.
}
```

Add:

```csharp
public void RecordResponse(ExecuteToolCallResult result)
{
    SetTagMaybe(
        OpenTelemetryConstants.GenAiToolCallResultKey,
        ExecuteToolPayloadSerializer.Serialize(result));
}
```

Throw `ArgumentNullException` only when the typed overload itself receives a
null model; customer values inside the model remain non-throwing.

- [ ] **Step 5: Run scope and compatibility tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ScopeTests|FullyQualifiedName~TraceContextPropagationTest"
```

Expected: all selected tests pass, including existing string behavior.

- [ ] **Step 6: Commit scope integration**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Contracts\ToolCallDetails.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\Tracing\Scopes\ExecuteToolScope.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Tracing\Scopes\ExecuteToolScopeTest.cs
git commit -m "Support typed execute tool span payloads" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 9b834f8f-58fc-46f9-b92a-35a5c4890e2e"
```

---

### Task 4: Integrate Typed Results with the ETW Path

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/DTOs/Builders/ExecuteToolDataBuilder.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/IA365EtwLogger.cs`
- Modify: `src/Microsoft.OpenTelemetry/Agent365/Runtime/Etw/A365EtwLogger.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/DTOs/Builders/ExecuteToolDataBuilderTests.cs`
- Modify: `test/Microsoft.OpenTelemetry.Agent365.Tests/Runtime/Etw/EtwLoggingBuilderTests.cs`

**Interfaces:**
- Consumes: typed arguments from `ToolCallDetails` and `ExecuteToolCallResult`.
- Produces: unambiguous typed `Build` and `LogToolCall` overloads with result as the second parameter.
- Preserves: existing optional string response parameter and existing call sites.

- [ ] **Step 1: Write failing builder test**

Add:

```csharp
[TestMethod]
public void Build_WithTypedPayloads_UsesSchemaSerializer()
{
    var arguments = new ExecuteToolCallArguments
    {
        Action = ToolCallAction.Read,
        Resources = new List<ToolCallResource>(),
        Parameters = new Dictionary<string, object?>(),
    };
    var result = new ExecuteToolCallResult
    {
        Outcome = new ToolCallResultOutcome
        {
            Status = ToolCallOutcomeStatus.Success,
        },
    };

    var data = ExecuteToolDataBuilder.Build(
        new ToolCallDetails("tool", arguments),
        result,
        new AgentDetails("agent"),
        "conversation");

    using var argumentsJson = JsonDocument.Parse(
        (string)data.Attributes[OpenTelemetryConstants.GenAiToolArgumentsKey]!);
    using var resultJson = JsonDocument.Parse(
        (string)data.Attributes[OpenTelemetryConstants.GenAiToolCallResultKey]!);

    argumentsJson.RootElement.GetProperty("action").GetString().Should().Be("read");
    resultJson.RootElement.GetProperty("outcome")
        .GetProperty("status").GetString().Should().Be("success");
}
```

- [ ] **Step 2: Write failing ETW test**

Add a test to `EtwLoggingBuilderTests.cs`:

```csharp
[TestMethod]
public void Build_WritesTypedToolResultToEtw()
{
    using var listener = new TestEventListener();
    listener.EnableEvents(EtwEventSource.Log, EventLevel.Informational);
    using var provider = BuildProvider();
    var logger = provider.GetRequiredService<IA365EtwLogger<EtwLoggingBuilderTests>>();
    var result = new ExecuteToolCallResult
    {
        Outcome = new ToolCallResultOutcome
        {
            Status = ToolCallOutcomeStatus.Success,
        },
        ["provider_summary"] = "ok",
    };

    logger.LogToolCall(
        new ToolCallDetails("tool-a", (string?)null),
        result,
        new AgentDetails("agent-id"),
        "conv-tool-typed");

    var evt = listener.Events.Find(e => e.EventId == 2000);
    var payload = JsonDocument.Parse((string)evt!.Payload![0]!);
    var attributes = payload.RootElement.GetProperty("Attributes");
    using var resultJson = JsonDocument.Parse(
        attributes.GetProperty(OpenTelemetryConstants.GenAiToolCallResultKey).GetString()!);

    resultJson.RootElement.GetProperty("provider_summary").GetString().Should().Be("ok");
    resultJson.RootElement.GetProperty("outcome")
        .GetProperty("status").GetString().Should().Be("success");
}
```

- [ ] **Step 3: Run builder and ETW tests and verify they fail**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolDataBuilderTests|FullyQualifiedName~EtwLoggingBuilderTests"
```

Expected: compilation fails because typed overloads do not exist.

- [ ] **Step 4: Add the typed builder overload**

Add this overload before the existing `Build` method:

```csharp
public static ExecuteToolData Build(
    ToolCallDetails toolCallDetails,
    ExecuteToolCallResult result,
    AgentDetails agentDetails,
    string conversationId,
    DateTimeOffset? startTime = null,
    DateTimeOffset? endTime = null,
    string? spanId = null,
    string? parentSpanId = null,
    Channel? channel = null,
    CallerDetails? callerDetails = null,
    IDictionary<string, object?>? extraAttributes = null,
    string? spanKind = null,
    string? traceId = null,
    Exception? error = null)
{
    var attributes = BuildAttributes(
        toolCallDetails,
        agentDetails,
        conversationId,
        responseContent: null,
        channel,
        callerDetails,
        extraAttributes);

    AddIfNotNull(
        attributes,
        OpenTelemetryConstants.GenAiToolCallResultKey,
        ExecuteToolPayloadSerializer.Serialize(result));

    return ApplyStatus(
        new ExecuteToolData(
            attributes,
            startTime,
            endTime,
            spanId,
            parentSpanId,
            spanKind,
            traceId),
        error);
}
```

Update `AddToolDetails` to use the same typed-arguments precedence implemented
in `ExecuteToolScope`. Do not alter the existing response string logic.

- [ ] **Step 5: Add compatibility-safe typed ETW support**

Keep `IA365EtwLogger<T>` unchanged and add a public extension method:

```csharp
public static void LogToolCall<T>(
    this IA365EtwLogger<T> logger,
    ToolCallDetails toolCallDetails,
    ExecuteToolCallResult result,
    AgentDetails agentDetails,
    string conversationId,
    DateTimeOffset? startTime = null,
    DateTimeOffset? endTime = null,
    string? spanId = null,
    string? parentSpanId = null,
    Channel? channel = null,
    CallerDetails? callerDetails = null,
    string? traceId = null,
    Exception? error = null);
```

The extension method should serialize `result` with
`ExecuteToolPayloadSerializer` and then call the existing interface overload:

```csharp
logger.LogToolCall(
    toolCallDetails,
    agentDetails,
    conversationId,
    ExecuteToolPayloadSerializer.Serialize(result),
    startTime,
    endTime,
    spanId,
    parentSpanId,
    channel,
    callerDetails,
    traceId,
    error);
```

Keep the same public signature on `A365EtwLogger<T>` and implement it with:

```csharp
var data = ExecuteToolDataBuilder.Build(
    toolCallDetails,
    result,
    agentDetails,
    conversationId,
    startTime,
    endTime,
    spanId,
    parentSpanId,
    channel,
    callerDetails: callerDetails,
    traceId: traceId,
    error: error);

logger.Log(
    LogLevel.Information,
    ExecuteToolEventId,
    data.ToDictionary(),
    null,
    LogFormatter);
```

Keep the existing `LogToolCall(ToolCallDetails, AgentDetails, string, string?,
...)` method unchanged.

- [ ] **Step 6: Run builder and ETW tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolDataBuilderTests|FullyQualifiedName~EtwLoggingBuilderTests|FullyQualifiedName~EtwLoggerTests"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit ETW integration**

```powershell
git add src\Microsoft.OpenTelemetry\Agent365\Runtime\DTOs\Builders\ExecuteToolDataBuilder.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\Etw\IA365EtwLogger.cs src\Microsoft.OpenTelemetry\Agent365\Runtime\Etw\A365EtwLogger.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\DTOs\Builders\ExecuteToolDataBuilderTests.cs test\Microsoft.OpenTelemetry.Agent365.Tests\Runtime\Etw\EtwLoggingBuilderTests.cs
git commit -m "Support typed execute tool ETW results" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 9b834f8f-58fc-46f9-b92a-35a5c4890e2e"
```

---

### Task 5: Update Public API, Documentation, and Validate

**Files:**
- Modify: `src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`
- Modify: `docs/agent365-getting-started.md`
- Test: all files changed in Tasks 1-4

**Interfaces:**
- Consumes: final compiler-reported public API signatures.
- Produces: documented typed usage and clean repository validation.

- [ ] **Step 1: Run the library build to discover public API analyzer entries**

Run:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --framework net8.0 --no-restore
```

Expected: build fails only with `RS0016` entries identifying the new public API.

- [ ] **Step 2: Add exact public API entries**

Copy the analyzer-reported signatures into
`src/Microsoft.OpenTelemetry/.publicApi/PublicAPI.Unshipped.txt`, preserving
ordinal sorting within the file. Include:

- all three enums and their values;
- all eleven dictionary-backed public classes;
- both constructors for each class;
- every typed convenience property getter/setter;
- `ToolCallDetails.ToolCallArguments`;
- the typed `ToolCallDetails` constructor;
- `ExecuteToolScope.RecordResponse(ExecuteToolCallResult)`;
- the typed builder overload; and
- both typed ETW logger overloads.

Do not manually invent nullability markers; use the analyzer output.

- [ ] **Step 3: Add typed documentation example**

In the execute-tool section near the existing `ExecuteToolScope.Start` example,
add:

```csharp
var arguments = new ExecuteToolCallArguments
{
    Action = ToolCallAction.Read,
    Resources = new List<ToolCallResource>
    {
        new()
        {
            Id = "sharepoint://contoso.sharepoint.com/items/01ABCDEF",
            Uri = "https://contoso.sharepoint.com/Architecture.docx",
            Name = "Architecture.docx",
            Type = "document",
            Provider = "microsoft.sharepoint",
            Identifiers = new List<ToolCallIdentifier>
            {
                new()
                {
                    Type = "microsoft.graph.drive_item_id",
                    Value = "01ABCDEF",
                },
            },
            Container = new ToolCallContainer
            {
                Id = "sharepoint://contoso.sharepoint.com/sites/Engineering",
                Uri = "https://contoso.sharepoint.com/sites/Engineering",
                Type = "site",
                ["tenant_id"] = "contoso",
            },
        },
    },
    Parameters = new Dictionary<string, object?>
    {
        ["format"] = "text",
        ["includeMetadata"] = true,
    },
};

using var scope = ExecuteToolScope.Start(
    request,
    new ToolCallDetails("sharepoint_get_document", arguments),
    agentDetails);

var result = new ExecuteToolCallResult
{
    Outcome = new ToolCallResultOutcome
    {
        Status = ToolCallOutcomeStatus.Success,
    },
    Resources = new List<ToolCallResultResource>(),
    Data = new Dictionary<string, object?>(),
    Pagination = new ToolCallResultPagination
    {
        HasMore = false,
        TotalCount = 0,
    },
};

result["provider_summary"] = "Document retrieved";
scope.RecordResponse(result);
```

Immediately after the example, document:

- the attributes contain JSON-serialized strings;
- every model is dictionary-backed, including identifiers and containers;
- direct indexer writes can add or replace standard fields;
- existing string and `IDictionary<string, object>` overloads remain supported.

- [ ] **Step 4: Run targeted execute-tool tests**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj --framework net8.0 --filter "FullyQualifiedName~ExecuteToolJsonModelsTests|FullyQualifiedName~ExecuteToolPayloadSerializerTests|FullyQualifiedName~ExecuteToolScopeTest|FullyQualifiedName~ExecuteToolDataBuilderTests|FullyQualifiedName~EtwLoggingBuilderTests|FullyQualifiedName~EtwLoggerTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Build every library target**

Run:

```powershell
dotnet build src\Microsoft.OpenTelemetry\Microsoft.OpenTelemetry.csproj --no-restore
```

Expected: build succeeds for `netstandard2.0` and `net8.0` with zero warnings and zero errors.

- [ ] **Step 6: Run the complete Agent365 test project**

Run:

```powershell
dotnet test test\Microsoft.OpenTelemetry.Agent365.Tests\Microsoft.OpenTelemetry.Agent365.Tests.csproj
```

Expected: all tests pass for `net8.0` and `net10.0`.

- [ ] **Step 7: Check formatting and diff integrity**

Run:

```powershell
dotnet format Microsoft.OpenTelemetry.slnx --verify-no-changes --no-restore
git --no-pager diff --check
```

Expected: formatter reports no files requiring changes, and `git diff --check`
prints no output.

- [ ] **Step 8: Commit API and documentation updates**

```powershell
git add src\Microsoft.OpenTelemetry\.publicApi\PublicAPI.Unshipped.txt docs\agent365-getting-started.md
git commit -m "Document execute tool JSON schema models" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 9b834f8f-58fc-46f9-b92a-35a5c4890e2e"
```
