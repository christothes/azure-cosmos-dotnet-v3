﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionContext
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class CatchAllCosmosQueryExecutionContext : CosmosQueryExecutionContext
    {
        private readonly CosmosQueryExecutionContext cosmosQueryExecutionContext;
        private bool hitException;

        public CatchAllCosmosQueryExecutionContext(
            CosmosQueryExecutionContext cosmosQueryExecutionContext)
        {
            if (cosmosQueryExecutionContext == null)
            {
                throw new ArgumentNullException(nameof(cosmosQueryExecutionContext));
            }

            this.cosmosQueryExecutionContext = cosmosQueryExecutionContext;
        }

        public override bool IsDone => this.hitException || this.cosmosQueryExecutionContext.IsDone;

        public override void Dispose()
        {
            this.cosmosQueryExecutionContext.Dispose();
        }

        public override async Task<QueryResponseCore> ExecuteNextAsync(CancellationToken cancellationToken)
        {
            if (this.IsDone)
            {
                throw new InvalidOperationException(
                    $"Can not {nameof(ExecuteNextAsync)} from a {nameof(CosmosQueryExecutionContext)} where {nameof(this.IsDone)}.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            QueryResponseCore queryResponseCore;
            try
            {
                queryResponseCore = await this.cosmosQueryExecutionContext.ExecuteNextAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                queryResponseCore = QueryResponseFactory.CreateFromException(ex);
            }

            if (!queryResponseCore.IsSuccess)
            {
                this.hitException = true;
            }

            return queryResponseCore;
        }

        public override bool TryGetContinuationToken(out string continuationToken)
        {
            return this.cosmosQueryExecutionContext.TryGetContinuationToken(out continuationToken);
        }
    }
}
