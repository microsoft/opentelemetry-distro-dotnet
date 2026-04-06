// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.OpenTelemetry.Agent365.Tracing.Contracts
{
    /// <summary>
    /// Supported inference operation types for generative AI.
    /// </summary>
    public enum InferenceOperationType
    {
        /// <summary>
        /// Chat-based inference operation.
        /// </summary>
        Chat,
        
        /// <summary>
        /// Text completion inference operation.
        /// </summary>
        TextCompletion,
        
        /// <summary>
        /// Content generation inference operation.
        /// </summary>
        GenerateContent
    }
}