// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Agents.A365.Observability.Runtime.Etw;
using System.Runtime.CompilerServices;

// PublicApiAnalyzers do not model forwarded symbols correctly; keep the forward local and
// track the concrete API on the owning assembly instead.
#pragma warning disable RS0016, RS0017
[assembly: TypeForwardedTo(typeof(EtwEventSource))]
#pragma warning restore RS0016, RS0017
