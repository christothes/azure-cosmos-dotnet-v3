﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Query;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using PartitionKeyDefinition = Documents.PartitionKeyDefinition;

    internal sealed class QueryPlanHandler
    {
        private readonly CosmosQueryClient queryClient;

        public QueryPlanHandler(CosmosQueryClient queryClient)
        {
            if (queryClient == null)
            {
                throw new ArgumentNullException($"{nameof(queryClient)}");
            }

            this.queryClient = queryClient;
        }

        public async Task<TryCatch<PartitionedQueryExecutionInfo>> TryGetQueryPlanAsync(
            SqlQuerySpec sqlQuerySpec,
            PartitionKeyDefinition partitionKeyDefinition,
            QueryFeatures supportedQueryFeatures,
            bool hasLogicalPartitionKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sqlQuerySpec == null)
            {
                throw new ArgumentNullException($"{nameof(sqlQuerySpec)}");
            }

            if (partitionKeyDefinition == null)
            {
                throw new ArgumentNullException($"{nameof(partitionKeyDefinition)}");
            }

            TryCatch<PartitionedQueryExecutionInfo> tryGetQueryInfo = await this.TryGetQueryInfoAsync(
                sqlQuerySpec,
                partitionKeyDefinition,
                hasLogicalPartitionKey,
                cancellationToken);
            if (!tryGetQueryInfo.Succeeded)
            {
                return tryGetQueryInfo;
            }

            if (QueryPlanExceptionFactory.TryGetUnsupportedException(
                tryGetQueryInfo.Result.QueryInfo,
                supportedQueryFeatures,
                out Exception queryPlanHandlerException))
            {
                return TryCatch<PartitionedQueryExecutionInfo>.FromException(queryPlanHandlerException);
            }

            return tryGetQueryInfo;
        }

        /// <summary>
        /// Used in the compute gateway to support legacy gateways query execution pattern.
        /// </summary>
        public async Task<((Exception, PartitionedQueryExecutionInfo), bool)> TryGetQueryInfoAndIfSupportedAsync(
            QueryFeatures supportedQueryFeatures,
            SqlQuerySpec sqlQuerySpec,
            PartitionKeyDefinition partitionKeyDefinition,
            bool hasLogicalPartitionKey,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (sqlQuerySpec == null)
            {
                throw new ArgumentNullException(nameof(sqlQuerySpec));
            }

            if (partitionKeyDefinition == null)
            {
                throw new ArgumentNullException(nameof(partitionKeyDefinition));
            }

            cancellationToken.ThrowIfCancellationRequested();

            TryCatch<PartitionedQueryExecutionInfo> tryGetQueryInfo = await this.TryGetQueryInfoAsync(
                sqlQuerySpec,
                partitionKeyDefinition,
                hasLogicalPartitionKey,
                cancellationToken);
            if (!tryGetQueryInfo.Succeeded)
            {
                // Failed to get QueryInfo, so vacously not supported.
                return ((tryGetQueryInfo.Exception, null), false);
            }

            QueryFeatures neededQueryFeatures = QueryPlanSupportChecker.GetNeededQueryFeatures(
                tryGetQueryInfo.Result.QueryInfo,
                supportedQueryFeatures);
            return ((null, tryGetQueryInfo.Result), neededQueryFeatures == QueryFeatures.None);
        }

        private async Task<TryCatch<PartitionedQueryExecutionInfo>> TryGetQueryInfoAsync(
            SqlQuerySpec sqlQuerySpec,
            PartitionKeyDefinition partitionKeyDefinition,
            bool hasLogicalPartitionKey,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            TryCatch<PartitionedQueryExecutionInfo> tryGetPartitoinedQueryExecutionInfo = await this.queryClient.TryGetPartitionedQueryExecutionInfoAsync(
                sqlQuerySpec: sqlQuerySpec,
                partitionKeyDefinition: partitionKeyDefinition,
                requireFormattableOrderByQuery: true,
                isContinuationExpected: false,
                allowNonValueAggregateQuery: true,
                hasLogicalPartitionKey: hasLogicalPartitionKey,
                cancellationToken: cancellationToken);

            return tryGetPartitoinedQueryExecutionInfo;
        }

        private static class QueryPlanSupportChecker
        {
            public static QueryFeatures GetNeededQueryFeatures(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeededQueryFeaturesIfAggregateQuery(queryInfo, supportedQueryFeatures);
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeededQueryFeaturesIfDistinctQuery(queryInfo, supportedQueryFeatures);
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeedQueryFeaturesIfGroupByQuery(queryInfo, supportedQueryFeatures);
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeededQueryFeaturesIfOffsetLimitQuery(queryInfo, supportedQueryFeatures);
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeededQueryFeaturesIfOrderByQuery(queryInfo, supportedQueryFeatures);
                neededQueryFeatures |= QueryPlanSupportChecker.GetNeededQueryFeaturesIfTopQuery(queryInfo, supportedQueryFeatures);

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeededQueryFeaturesIfAggregateQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasAggregates)
                {
                    bool isSingleAggregate = (queryInfo.Aggregates.Length == 1)
                        || (queryInfo.GroupByAliasToAggregateType.Values.Where(aggregateOperator => aggregateOperator.HasValue).Count() == 1);
                    if (isSingleAggregate)
                    {
                        if (queryInfo.HasSelectValue)
                        {
                            if (!supportedQueryFeatures.HasFlag(QueryFeatures.Aggregate))
                            {
                                neededQueryFeatures |= QueryFeatures.Aggregate;
                            }
                        }
                        else
                        {
                            if (!supportedQueryFeatures.HasFlag(QueryFeatures.NonValueAggregate))
                            {
                                neededQueryFeatures |= QueryFeatures.NonValueAggregate;
                            }
                        }
                    }
                    else
                    {
                        if (!supportedQueryFeatures.HasFlag(QueryFeatures.NonValueAggregate))
                        {
                            neededQueryFeatures |= QueryFeatures.NonValueAggregate;
                        }

                        if (!supportedQueryFeatures.HasFlag(QueryFeatures.MultipleAggregates))
                        {
                            neededQueryFeatures |= QueryFeatures.MultipleAggregates;
                        }
                    }
                }

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeededQueryFeaturesIfDistinctQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasDistinct)
                {
                    if (!supportedQueryFeatures.HasFlag(QueryFeatures.Distinct))
                    {
                        neededQueryFeatures |= QueryFeatures.Distinct;
                    }
                }

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeededQueryFeaturesIfTopQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasTop)
                {
                    if (!supportedQueryFeatures.HasFlag(QueryFeatures.Top))
                    {
                        neededQueryFeatures |= QueryFeatures.Top;
                    }
                }

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeededQueryFeaturesIfOffsetLimitQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasLimit || queryInfo.HasOffset)
                {
                    if (!supportedQueryFeatures.HasFlag(QueryFeatures.OffsetAndLimit))
                    {
                        neededQueryFeatures |= QueryFeatures.OffsetAndLimit;
                    }
                }

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeedQueryFeaturesIfGroupByQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasGroupBy)
                {
                    if (!supportedQueryFeatures.HasFlag(QueryFeatures.GroupBy))
                    {
                        neededQueryFeatures |= QueryFeatures.GroupBy;
                    }
                }

                return neededQueryFeatures;
            }

            private static QueryFeatures GetNeededQueryFeaturesIfOrderByQuery(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures)
            {
                QueryFeatures neededQueryFeatures = QueryFeatures.None;
                if (queryInfo.HasOrderBy)
                {
                    if (queryInfo.OrderByExpressions.Length == 1)
                    {
                        if (!supportedQueryFeatures.HasFlag(QueryFeatures.OrderBy))
                        {
                            neededQueryFeatures |= QueryFeatures.OrderBy;
                        }
                    }
                    else
                    {
                        if (!supportedQueryFeatures.HasFlag(QueryFeatures.MultipleOrderBy))
                        {
                            neededQueryFeatures |= QueryFeatures.MultipleOrderBy;
                        }
                    }
                }

                return neededQueryFeatures;
            }
        }

        private static class QueryPlanExceptionFactory
        {
            private static readonly IReadOnlyList<QueryFeatures> QueryFeatureList = (QueryFeatures[])Enum.GetValues(typeof(QueryFeatures));
            private static readonly ReadOnlyDictionary<QueryFeatures, ArgumentException> FeatureToUnsupportedException = new ReadOnlyDictionary<QueryFeatures, ArgumentException>(
                QueryFeatureList
                .ToDictionary(
                    x => x,
                    x => new ArgumentException(QueryPlanExceptionFactory.FormatExceptionMessage(x.ToString()))));

            public static bool TryGetUnsupportedException(
                QueryInfo queryInfo,
                QueryFeatures supportedQueryFeatures,
                out Exception queryPlanHandlerException)
            {
                QueryFeatures neededQueryFeatures = QueryPlanSupportChecker.GetNeededQueryFeatures(
                    queryInfo,
                    supportedQueryFeatures);
                if (neededQueryFeatures != QueryFeatures.None)
                {
                    List<Exception> queryPlanHandlerExceptions = new List<Exception>();
                    foreach (QueryFeatures queryFeature in QueryPlanExceptionFactory.QueryFeatureList)
                    {
                        if ((neededQueryFeatures & queryFeature) == queryFeature)
                        {
                            Exception unsupportedFeatureException = QueryPlanExceptionFactory.FeatureToUnsupportedException[queryFeature];
                            queryPlanHandlerExceptions.Add(unsupportedFeatureException);
                        }
                    }

                    queryPlanHandlerException = new QueryPlanHandlerException(queryPlanHandlerExceptions);
                    return true;
                }

                queryPlanHandlerException = default;
                return false;
            }

            private static string FormatExceptionMessage(string feature)
            {
                return $"Query contained {feature}, which the calling client does not support.";
            }

            private sealed class QueryPlanHandlerException : AggregateException
            {
                private const string QueryContainsUnsupportedFeaturesExceptionMessage = "Query contains 1 or more unsupported features. Upgrade your SDK to a version that does support the requested features:";
                public QueryPlanHandlerException(IEnumerable<Exception> innerExceptions)
                    : base(
                          QueryContainsUnsupportedFeaturesExceptionMessage
                              + Environment.NewLine
                              + string.Join(Environment.NewLine, innerExceptions.Select(innerException => innerException.Message)),
                          innerExceptions)
                {
                }
            }
        }
    }
}