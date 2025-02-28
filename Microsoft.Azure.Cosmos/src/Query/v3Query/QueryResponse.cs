//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Query;
    using Microsoft.Azure.Documents;

    /// <summary>
    /// Represents the template class used by feed methods (enumeration operations) for the Azure Cosmos DB service.
    /// </summary>
    internal class QueryResponse : ResponseMessage
    {
        private readonly Lazy<MemoryStream> memoryStream;

        /// <summary>
        /// Used for unit testing only
        /// </summary>
        internal QueryResponse()
        {
        }

        private QueryResponse(
            IEnumerable<CosmosElement> result,
            int count,
            long responseLengthBytes,
            CosmosQueryResponseMessageHeaders responseHeaders,
            HttpStatusCode statusCode,
            RequestMessage requestMessage,
            CosmosDiagnostics diagnostics,
            string errorMessage,
            Error error,
            Lazy<MemoryStream> memoryStream,
            CosmosSerializationFormatOptions serializationOptions)
            : base(
                statusCode: statusCode,
                requestMessage: requestMessage,
                errorMessage: errorMessage,
                error: error,
                headers: responseHeaders,
                diagnostics: diagnostics)
        {
            this.CosmosElements = result;
            this.Count = count;
            this.ResponseLengthBytes = responseLengthBytes;
            this.memoryStream = memoryStream;
            this.CosmosSerializationOptions = serializationOptions;
        }

        public int Count { get; }

        public override Stream Content
        {
            get
            {
                return this.memoryStream?.Value;
            }
        }

        internal virtual IEnumerable<CosmosElement> CosmosElements { get; }

        internal virtual CosmosQueryResponseMessageHeaders QueryHeaders => (CosmosQueryResponseMessageHeaders)this.Headers;

        /// <summary>
        /// Gets the response length in bytes
        /// </summary>
        /// <remarks>
        /// This value is only set for Direct mode.
        /// </remarks>
        internal long ResponseLengthBytes { get; }

        internal virtual CosmosSerializationFormatOptions CosmosSerializationOptions { get; }

        internal bool GetHasMoreResults()
        {
            return !string.IsNullOrEmpty(this.Headers.ContinuationToken);
        }

        internal static QueryResponse CreateSuccess(
            IEnumerable<CosmosElement> result,
            int count,
            long responseLengthBytes,
            CosmosQueryResponseMessageHeaders responseHeaders,
            CosmosDiagnostics diagnostics,
            CosmosSerializationFormatOptions serializationOptions)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count must be positive");
            }

            if (responseLengthBytes < 0)
            {
                throw new ArgumentOutOfRangeException("responseLengthBytes must be positive");
            }

            Lazy<MemoryStream> memoryStream = new Lazy<MemoryStream>(() => CosmosElementSerializer.ToStream(
                       responseHeaders.ContainerRid,
                       result,
                       responseHeaders.ResourceType,
                       serializationOptions));

            QueryResponse cosmosQueryResponse = new QueryResponse(
               result: result,
               count: count,
               responseLengthBytes: responseLengthBytes,
               responseHeaders: responseHeaders,
               diagnostics: diagnostics,
               statusCode: HttpStatusCode.OK,
               errorMessage: null,
               error: null,
               requestMessage: null,
               memoryStream: memoryStream,
               serializationOptions: serializationOptions);

            return cosmosQueryResponse;
        }

        internal static QueryResponse CreateFailure(
            CosmosQueryResponseMessageHeaders responseHeaders,
            HttpStatusCode statusCode,
            RequestMessage requestMessage,
            string errorMessage,
            Error error,
            CosmosDiagnostics diagnostics)
        {
            QueryResponse cosmosQueryResponse = new QueryResponse(
                    result: Enumerable.Empty<CosmosElement>(),
                    count: 0,
                    responseLengthBytes: 0,
                    responseHeaders: responseHeaders,
                    diagnostics: diagnostics,
                    statusCode: statusCode,
                    errorMessage: errorMessage,
                    error: error,
                    requestMessage: requestMessage,
                    memoryStream: null,
                    serializationOptions: null);

            return cosmosQueryResponse;
        }
    }

    /// <summary>
    /// The cosmos query response
    /// </summary>
    /// <typeparam name="T">The type for the query response.</typeparam>
    internal class QueryResponse<T> : FeedResponse<T>
    {
        private readonly IEnumerable<CosmosElement> cosmosElements;
        private readonly CosmosSerializer jsonSerializer;
        private readonly CosmosSerializationFormatOptions serializationOptions;
        private IEnumerable<T> resources;

        private QueryResponse(
            HttpStatusCode httpStatusCode,
            IEnumerable<CosmosElement> cosmosElements,
            CosmosQueryResponseMessageHeaders responseMessageHeaders,
            CosmosDiagnostics diagnostics,
            CosmosSerializer jsonSerializer,
            CosmosSerializationFormatOptions serializationOptions)
        {
            this.cosmosElements = cosmosElements;
            this.QueryHeaders = responseMessageHeaders;
            this.Diagnostics = diagnostics;
            this.jsonSerializer = jsonSerializer;
            this.serializationOptions = serializationOptions;
            this.StatusCode = httpStatusCode;
        }

        public override string ContinuationToken => this.Headers.ContinuationToken;

        public override double RequestCharge => this.Headers.RequestCharge;

        public override Headers Headers => this.QueryHeaders;

        public override HttpStatusCode StatusCode { get; }

        public override CosmosDiagnostics Diagnostics { get; }

        public override int Count => this.cosmosElements?.Count() ?? 0;

        internal CosmosQueryResponseMessageHeaders QueryHeaders { get; }

        public override IEnumerator<T> GetEnumerator()
        {
            return this.Resource.GetEnumerator();
        }

        public override IEnumerable<T> Resource
        {
            get
            {
                if (this.resources == null)
                {
                    if (typeof(T) == typeof(CosmosElement))
                    {
                        this.resources = this.cosmosElements.Cast<T>();
                    }
                    else
                    {
                        this.resources = CosmosElementSerializer.Deserialize<T>(
                            this.QueryHeaders.ContainerRid,
                            this.cosmosElements,
                            this.QueryHeaders.ResourceType,
                            this.jsonSerializer,
                            this.serializationOptions);
                    }
                }

                return this.resources;
            }
        }

        internal static QueryResponse<TInput> CreateResponse<TInput>(
            QueryResponse cosmosQueryResponse,
            CosmosSerializer jsonSerializer)
        {
            QueryResponse<TInput> queryResponse;
            using (cosmosQueryResponse)
            {
                cosmosQueryResponse.EnsureSuccessStatusCode();
                queryResponse = new QueryResponse<TInput>(
                    httpStatusCode: cosmosQueryResponse.StatusCode,
                    cosmosElements: cosmosQueryResponse.CosmosElements,
                    responseMessageHeaders: cosmosQueryResponse.QueryHeaders,
                    diagnostics: cosmosQueryResponse.Diagnostics,
                    jsonSerializer: jsonSerializer,
                    serializationOptions: cosmosQueryResponse.CosmosSerializationOptions);
            }
            return queryResponse;
        }
    }
}