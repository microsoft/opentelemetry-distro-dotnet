// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Agents.A365.Observability.Runtime.Validation;

/// <summary>
/// Thrown when a validation session cannot produce a report because a
/// caller-supplied callback -- <see cref="A365ValidationOptions.SpanFilter"/>
/// or a suppression predicate -- threw. The original failure is always
/// preserved as <see cref="Exception.InnerException"/>.
/// </summary>
/// <remarks>
/// This is distinct from <see cref="A365ValidationException"/>, which reports
/// a validation report that contains active errors. It is also distinct from
/// an exception thrown by the validated action itself: that exception is
/// propagated unchanged, never wrapped.
/// </remarks>
public sealed class A365ValidationExecutionException : Exception
{
    internal A365ValidationExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
