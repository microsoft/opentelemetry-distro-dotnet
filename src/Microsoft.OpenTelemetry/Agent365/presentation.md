# A365 Observability SDK → Microsoft OTel Distro
## API Alignment for Standards Compliance

**Rajkumar Rangaraj** | April 14, 2026  
*Microsoft OpenTelemetry Maintainer*

---

## Agenda

1. Context & Goal
2. Concern 1: Parallel Span-Creation API → Solution
3. Concern 2: Namespace Pollution → Solution
4. Concern 3: Documentation Gap → Solution
5. Impact & Risk if Unchanged
6. Ask & Next Steps

---

## 1. Context & Goal

### What We're Doing
- Integrating **A365 Observability SDK** capabilities into the **Microsoft OpenTelemetry Distro**
- Enabling agent developers to produce telemetry for store publishing, Defender, and Admin Center

### The Constraint
- As an **OpenTelemetry maintainer**, I must ensure anything shipped under the Microsoft OTel Distro upholds **vendor-neutrality** — the foundational guarantee of OpenTelemetry
- The current A365 SDK API surface introduces patterns that conflict with this guarantee

### Why It Matters Now
- Integration work is underway — changes are cheaper today than after GA
- Three SDKs affected: **.NET, Python, JavaScript**

---

## 2. Concerns & Solutions

---

### Concern 1: Parallel Span-Creation API

**Current State** — A365 introduces proprietary scope classes that wrap `ActivitySource`:

```csharp
// A365 SDK — developer MUST use these to produce compliant telemetry
using var scope = InvokeAgentScope.Start(request, scopeDetails, agentDetails, callerDetails);
scope.RecordResponse(response);

using var tool = ExecuteToolScope.Start(request, toolDetails, agentDetails);
tool.RecordResult(result);

using var inference = InferenceScope.Start(request, inferenceDetails, agentDetails);
inference.RecordOutputMessages(messages);
```

**The Problem:**
| | Standard OTel | A365 SDK Today |
|---|---|---|
| Span creation | `ActivitySource.StartActivity()` | `InvokeAgentScope.Start()` |
| Attribute setting | `activity.SetTag("key", value)` | Hidden inside scope constructor |
| Lifecycle | `using var activity = ...` | `using var scope = ...` |

> Developers must learn and depend on **Microsoft-specific classes** to produce compliant telemetry — regardless of language. This is a **replacement**, not an extension, of the OTel API.

#### Solution: Enrichment, Not Replacement

Follow the same pattern as **Azure Monitor Distro** — enrich telemetry via a `SpanProcessor`, don't replace the creation API.

```
┌──────────────────────────────────────────────────────────┐
│                  Developer Code                          │
│                                                          │
│   // Standard OTel API — what developers write           │
│   using var activity = agentSource.StartActivity(        │
│       "invoke_agent MyAgent",                            │
│       ActivityKind.Client);                              │
│   activity?.SetTag("gen_ai.operation.name",              │
│       "invoke_agent");                                   │
│   activity?.SetTag("gen_ai.agent.name", "MyAgent");      │
│                                                          │
└──────────────────────┬───────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────┐
│          M365 SpanProcessor (auto-enrichment)            │
│                                                          │
│   // Adds microsoft.* attributes automatically           │
│   activity.SetTag("microsoft.tenant.id", tenantId);      │
│   activity.SetTag("microsoft.agent.user.id", auid);      │
│   activity.SetTag("microsoft.session.id", sessionId);    │
│                                                          │
└──────────────────────┬───────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────┐
│        OTel Pipeline (exporters, sampling, etc.)         │
│                                                          │
│   → Azure Monitor    → A365 Exporter    → Console        │
└──────────────────────────────────────────────────────────┘
```

**Precedent already exists**: `AgentFrameworkSpanProcessor` in the distro already follows this model — it hooks into `OnEnd()`, enriches spans with additional attributes, and requires no custom span-creation API.

**Action Required:**
- **SDK team** refactors M365-specific attribute enrichment into a `BaseProcessor<Activity>` SpanProcessor
- Scope classes can remain as *optional convenience wrappers* but must **not** be the required path
- Store validation must accept telemetry produced via standard OTel APIs

---

### Concern 2: Namespace Pollution in `gen_ai.*`

**Current attribute mapping in `OpenTelemetryConstants.cs`:**

| Attribute Key | OTel Standard? | Issue |
|---|---|---|
| `gen_ai.operation.name` | ✅ Yes | Standard GenAI semconv |
| `gen_ai.request.model` | ✅ Yes | Standard GenAI semconv |
| `gen_ai.usage.input_tokens` | ✅ Yes | Standard GenAI semconv |
| `gen_ai.agent.id` | ✅ Yes | Standard GenAI semconv |
| `gen_ai.agent.name` | ✅ Yes | Standard GenAI semconv |
| `gen_ai.tool.name` | ✅ Yes | Standard GenAI semconv |
| `microsoft.agent.user.id` | ✅ Correct | Vendor-prefixed |
| `microsoft.tenant.id` | ✅ Correct | Vendor-prefixed |
| `microsoft.session.id` | ✅ Correct | Vendor-prefixed |
| `gen_ai.agent.invocation_input` | ❌ **Not standard** | Should be `microsoft.*` |
| `gen_ai.agent.invocation_output` | ❌ **Not standard** | Should be `microsoft.*` |
| `gen_ai.conversation.id` | ⚠️ **Pending** | Not yet in semconv |
| `threat.diagnostics.summary` | ❌ **Not standard** | Should be `microsoft.*` |

> Microsoft-specific attributes under `gen_ai.*` risk **schema conflicts** with the upstream OpenTelemetry semantic conventions as GenAI semconv evolves.

#### Solution: Clear Namespace Separation

Move all non-standard attributes to vendor-prefixed namespaces (`microsoft.*` / `microsoft.a365.*`):

| Current Key | Action | New Key |
|---|---|---|
| `gen_ai.agent.invocation_input` | **Move** → vendor namespace | `microsoft.a365.agent.invocation_input` |
| `gen_ai.agent.invocation_output` | **Move** → vendor namespace | `microsoft.a365.agent.invocation_output` |
| `gen_ai.conversation.id` | **Keep** — track upstream semconv adoption | `gen_ai.conversation.id` |
| `threat.diagnostics.summary` | **Move** → vendor namespace | `microsoft.a365.threat.diagnostics.summary` |
| `gen_ai.agent.thought.process` | Already correct (`microsoft.a365.`) | No change |
| `microsoft.channel.*` | Already correct | No change |
| `microsoft.tenant.id` | Already correct | No change |

> **Good news:** Several attributes (tenant, channel, agent user, caller agent) have already been moved to `microsoft.*` — this is partial progress we can build on.

**Action Required:**
- **SDK team** updates `OpenTelemetryConstants.cs` across all 3 language SDKs
- Agree on final namespace mapping in **Week 1 working session**
- Coordinate with **Store Validation** team to accept both old and new keys during migration window

---

### Concern 3: Documentation Gap

**What Developers See Today:**
- A365-specific API as the *only* path to produce agent telemetry
- No reference to OTel GenAI semantic conventions
- No distinction between what is **OTel-standard** vs. **Microsoft-specific**

**What Developers Need:**
- Standard OTel API as the *primary* instrumentation path
- Clear table: "OTel standard attributes" vs. "M365-required attributes"
- Idiomatic examples per language (.NET, Python, JS)

#### Solution: Docs-First Approach

Rewrite the observability guide with this structure per scenario (agent invocation, tool execution, inference):

**1. Standard OTel instrumentation** (language-neutral concept + per-language examples)
```csharp
// .NET — standard OTel API
using var activity = agentSource.StartActivity("invoke_agent MyAgent", ActivityKind.Client);
activity?.SetTag("gen_ai.operation.name", "invoke_agent");
activity?.SetTag("gen_ai.agent.name", "MyAgent");
activity?.SetTag("gen_ai.agent.id", agentId);
```

**2. M365-required attributes** (clearly called out as Microsoft-specific)
```
// These are added automatically by the M365 SpanProcessor when using the Distro.
// If not using the Distro, set them manually:
microsoft.tenant.id          — required for store publishing
microsoft.agent.user.id      — required for Defender integration
microsoft.session.id         — required for Admin Center
```

**3. Certification checklist** — which attributes are required for store publishing

**Action Required:**
- **Docs team** rewrites the guide following this structure
- Include links to upstream [OTel GenAI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- Language-specific examples for **.NET, Python, JavaScript**

---

## 3. Impact & Risk if Unchanged

### If We Ship As-Is

| Risk | Impact |
|---|---|
| **Community trust** | OTel maintainers and ecosystem partners will flag vendor lock-in concerns |
| **Developer friction** | 3 SDKs × proprietary API = fragmented DX; developers can't reuse OTel knowledge |
| **Schema conflicts** | Upstream `gen_ai.*` semconv evolution will create breaking changes for A365 telemetry |
| **Adoption barrier** | Developers already using OTel must adopt a *second* instrumentation API |

### If We Align

| Benefit | Impact |
|---|---|
| **Zero new API to learn** | Developers use standard OTel — M365 enrichment is automatic |
| **Future-proof** | Attributes in `microsoft.*` won't conflict with upstream semconv |
| **Distro integration** | Clean integration path into Microsoft OTel Distro |
| **Broader reach** | Any OTel-instrumented agent can light up A365 scenarios |

---

## 4. Ask & Next Steps

### Immediate Ask
> **Identify the right contacts** from the A365 team across three areas:
> 1. **SDK** — to drive the SpanProcessor refactor
> 2. **Docs** — to rewrite the observability guide
> 3. **Store Validation** — to update certification checks

### Proposed Timeline

| Week | Milestone |
|---|---|
| **Week 1** | Working session: align on attribute namespace mapping |
| **Week 2–3** | SDK: implement SpanProcessor model; move attributes |
| **Week 3–4** | Docs: rewrite guide with standard OTel examples |
| **Week 4–5** | Validation: update store checks; end-to-end testing |

### Key Point
> All A365 observability requirements — store publishing, Defender integration, Admin Center visibility — **can be fully met** within the standard OTel model. This is about *how* we deliver, not *what* we deliver.

---

## Appendix: Reference Architecture

### Current (A365 SDK)
```
Developer → InvokeAgentScope.Start() → A365 Exporter → Store
                                                      → Defender
                                                      → Admin Center
```

### Proposed (OTel-Native)
```
Developer → ActivitySource.StartActivity()  ─┐
                                              ├→ M365 SpanProcessor (enrichment)
            BaggageMiddleware (context)      ─┘        │
                                                       ▼
                                              OTel Pipeline
                                              ├→ A365 Exporter → Store / Defender / Admin
                                              ├→ Azure Monitor
                                              └→ Any OTel Exporter
```

### Existing Precedent in the Distro
The `AgentFrameworkSpanProcessor` already follows this model:
- Hooks into `OnEnd()` lifecycle
- Enriches spans with additional attributes
- No custom span-creation API required

---

*"Meet developers where they are — on the OTel standard."*
