﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Resource.CosmosExceptions;

    internal abstract partial class DistinctDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        /// <summary>
        /// Compute implementation of DISTINCT.
        /// Here we never serialize the continuation token, but you can always retrieve it on demand with TryGetContinuationToken.
        /// </summary>
        private sealed class ComputeDistinctDocumentQueryExecutionComponent : DistinctDocumentQueryExecutionComponent
        {
            private static readonly string UseTryGetContinuationTokenMessage = $"Use {nameof(ComputeDistinctDocumentQueryExecutionComponent.TryGetContinuationToken)}";

            private ComputeDistinctDocumentQueryExecutionComponent(
                DistinctQueryType distinctQueryType,
                DistinctMap distinctMap,
                IDocumentQueryExecutionComponent source)
                : base(distinctMap, source)
            {
            }

            public static async Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateAsync(
                string requestContinuation,
                Func<string, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync,
                DistinctQueryType distinctQueryType)
            {
                DistinctContinuationToken distinctContinuationToken;
                if (requestContinuation != null)
                {
                    if (!DistinctContinuationToken.TryParse(requestContinuation, out distinctContinuationToken))
                    {
                        return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException($"Invalid {nameof(DistinctContinuationToken)}: {requestContinuation}"));
                    }
                }
                else
                {
                    distinctContinuationToken = new DistinctContinuationToken(sourceToken: null, distinctMapToken: null);
                }

                TryCatch<DistinctMap> tryCreateDistinctMap = DistinctMap.TryCreate(
                    distinctQueryType,
                    distinctContinuationToken.DistinctMapToken);
                if (!tryCreateDistinctMap.Succeeded)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(tryCreateDistinctMap.Exception);
                }

                TryCatch<IDocumentQueryExecutionComponent> tryCreateSource = await tryCreateSourceAsync(distinctContinuationToken.SourceToken);
                if (!tryCreateSource.Succeeded)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(tryCreateSource.Exception);
                }

                return TryCatch<IDocumentQueryExecutionComponent>.FromResult(
                    new ComputeDistinctDocumentQueryExecutionComponent(
                        distinctQueryType,
                        tryCreateDistinctMap.Result,
                        tryCreateSource.Result));
            }

            /// <summary>
            /// Drains a page of results returning only distinct elements.
            /// </summary>
            /// <param name="maxElements">The maximum number of items to drain.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>A page of distinct results.</returns>
            public override async Task<QueryResponseCore> DrainAsync(int maxElements, CancellationToken cancellationToken)
            {
                List<CosmosElement> distinctResults = new List<CosmosElement>();
                QueryResponseCore sourceResponse = await base.DrainAsync(maxElements, cancellationToken);

                if (!sourceResponse.IsSuccess)
                {
                    return sourceResponse;
                }

                foreach (CosmosElement document in sourceResponse.CosmosElements)
                {
                    if (this.distinctMap.Add(document, out UInt128 hash))
                    {
                        distinctResults.Add(document);
                    }
                }

                return QueryResponseCore.CreateSuccess(
                        result: distinctResults,
                        continuationToken: null,
                        disallowContinuationTokenMessage: ComputeDistinctDocumentQueryExecutionComponent.UseTryGetContinuationTokenMessage,
                        activityId: sourceResponse.ActivityId,
                        requestCharge: sourceResponse.RequestCharge,
                        diagnostics: sourceResponse.Diagnostics,
                        responseLengthBytes: sourceResponse.ResponseLengthBytes);
            }

            public override bool TryGetContinuationToken(out string continuationToken)
            {
                if (this.IsDone)
                {
                    continuationToken = null;
                    return true;
                }

                if (!this.Source.TryGetContinuationToken(out string sourceContinuationToken))
                {
                    continuationToken = default;
                    return false;
                }

                continuationToken = new DistinctContinuationToken(
                    sourceContinuationToken,
                    this.distinctMap.GetContinuationToken()).ToString();
                return true;
            }
        }
    }
}
