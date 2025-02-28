﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Collections;
    using Newtonsoft.Json;

    internal class GatewayStoreClient : TransportClient
    {
        private readonly ICommunicationEventSource eventSource;
        private HttpClient httpClient;
        private JsonSerializerSettings SerializerSettings;

        public GatewayStoreClient(
            HttpClient httpClient,
            ICommunicationEventSource eventSource,
            JsonSerializerSettings serializerSettings = null)
        {
            this.httpClient = httpClient;
            this.SerializerSettings = serializerSettings;
            this.eventSource = eventSource;
        }

        public async Task<DocumentServiceResponse> InvokeAsync(
           DocumentServiceRequest request,
           ResourceType resourceType,
           Uri physicalAddress,
           CancellationToken cancellationToken)
        {
            using (HttpResponseMessage responseMessage = await this.InvokeClientAsync(request, resourceType, physicalAddress, cancellationToken))
            {
                return await GatewayStoreClient.ParseResponseAsync(responseMessage, request.SerializerSettings ?? this.SerializerSettings, request);
            }
        }

        public static bool IsFeedRequest(OperationType requestOperationType)
        {
            return requestOperationType == OperationType.Create ||
                requestOperationType == OperationType.Upsert ||
                requestOperationType == OperationType.ReadFeed ||
                requestOperationType == OperationType.Query ||
                requestOperationType == OperationType.SqlQuery ||
                requestOperationType == OperationType.QueryPlan ||
                requestOperationType == OperationType.Batch;
        }

        internal override async Task<StoreResponse> InvokeStoreAsync(Uri baseAddress, ResourceOperation resourceOperation, DocumentServiceRequest request)
        {
            Uri physicalAddress = GatewayStoreClient.IsFeedRequest(request.OperationType) ?
                HttpTransportClient.GetResourceFeedUri(resourceOperation.resourceType, baseAddress, request) :
                HttpTransportClient.GetResourceEntryUri(resourceOperation.resourceType, baseAddress, request);

            using (HttpResponseMessage responseMessage = await this.InvokeClientAsync(request, resourceOperation.resourceType, physicalAddress, default(CancellationToken)))
            {
                return await HttpTransportClient.ProcessHttpResponse(request.ResourceAddress, string.Empty, responseMessage, physicalAddress, request);
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposable object returned by method")]
        internal Task<HttpResponseMessage> SendHttpAsync(HttpRequestMessage requestMessage, CancellationToken cancellationToken = default(CancellationToken))
        {
            return this.httpClient.SendHttpAsync(requestMessage, cancellationToken);
        }

        internal static async Task<DocumentServiceResponse> ParseResponseAsync(HttpResponseMessage responseMessage, JsonSerializerSettings serializerSettings = null, DocumentServiceRequest request = null)
        {
            using (responseMessage)
            {
                if ((int)responseMessage.StatusCode < 400)
                {
                    INameValueCollection headers = GatewayStoreClient.ExtractResponseHeaders(responseMessage);
                    Stream contentStream = await GatewayStoreClient.BufferContentIfAvailableAsync(responseMessage);
                    return new DocumentServiceResponse(contentStream, headers, responseMessage.StatusCode, serializerSettings);
                }
                else if (request != null
                    && request.IsValidStatusCodeForExceptionlessRetry((int)responseMessage.StatusCode))
                {
                    INameValueCollection headers = GatewayStoreClient.ExtractResponseHeaders(responseMessage);
                    Stream contentStream = await GatewayStoreClient.BufferContentIfAvailableAsync(responseMessage);
                    return new DocumentServiceResponse(contentStream, headers, responseMessage.StatusCode, serializerSettings);
                }
                else
                {
                    throw await GatewayStoreClient.CreateDocumentClientExceptionAsync(responseMessage);
                }
            }
        }

        internal static INameValueCollection ExtractResponseHeaders(HttpResponseMessage responseMessage)
        {
            INameValueCollection headers = new DictionaryNameValueCollection();

            foreach (KeyValuePair<string, IEnumerable<string>> headerPair in responseMessage.Headers)
            {
                if (string.Compare(headerPair.Key, HttpConstants.HttpHeaders.OwnerFullName, StringComparison.Ordinal) == 0)
                {
                    foreach (string val in headerPair.Value)
                    {
                        headers.Add(headerPair.Key, Uri.UnescapeDataString(val));
                    }
                }
                else
                {
                    foreach (string val in headerPair.Value)
                    {
                        headers.Add(headerPair.Key, val);
                    }
                }
            }

            if (responseMessage.Content != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> headerPair in responseMessage.Content.Headers)
                {
                    if (string.Compare(headerPair.Key, HttpConstants.HttpHeaders.OwnerFullName, StringComparison.Ordinal) == 0)
                    {
                        foreach (string val in headerPair.Value)
                        {
                            headers.Add(headerPair.Key, Uri.UnescapeDataString(val));
                        }
                    }
                    else
                    {
                        foreach (string val in headerPair.Value)
                        {
                            headers.Add(headerPair.Key, val);
                        }
                    }
                }
            }

            return headers;
        }

        internal static async Task<DocumentClientException> CreateDocumentClientExceptionAsync(HttpResponseMessage responseMessage)
        {
            // ensure there is no local ActivityId, since in Gateway mode ActivityId
            // should always come from message headers
            Trace.CorrelationManager.ActivityId = Guid.Empty;

            bool isNameBased = false;
            bool isFeed = false;
            string resourceTypeString;
            string resourceIdOrFullName;

            string resourceLink = responseMessage.RequestMessage.RequestUri.LocalPath;
            if (!PathsHelper.TryParsePathSegments(resourceLink, out isFeed, out resourceTypeString, out resourceIdOrFullName, out isNameBased))
            {
                // if resourceLink is invalid - we will not set resourceAddress in exception.
            }

            // If service rejects the initial payload like header is to large it will return an HTML error instead of JSON.
            if (string.Equals(responseMessage.Content?.Headers?.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                Stream readStream = await responseMessage.Content.ReadAsStreamAsync();
                Error error = Documents.Resource.LoadFrom<Error>(readStream);
                return new DocumentClientException(
                    error,
                    responseMessage.Headers,
                    responseMessage.StatusCode)
                {
                    StatusDescription = responseMessage.ReasonPhrase,
                    ResourceAddress = resourceIdOrFullName
                };
            }
            else
            {
                String message = await responseMessage.Content.ReadAsStringAsync();
                return new DocumentClientException(
                    message: message,
                    innerException: null,
                    responseHeaders: responseMessage.Headers,
                    statusCode: responseMessage.StatusCode,
                    requestUri: responseMessage.RequestMessage.RequestUri)
                {
                    StatusDescription = responseMessage.ReasonPhrase,
                    ResourceAddress = resourceIdOrFullName
                };
            }
        }

        internal static bool IsAllowedRequestHeader(string headerName)
        {
            if (!headerName.StartsWith("x-ms", StringComparison.OrdinalIgnoreCase))
            {
                switch (headerName)
                {
                    //Just flow the header which are settable at RequestMessage level and the one we care.
                    case HttpConstants.HttpHeaders.Authorization:
                    case HttpConstants.HttpHeaders.Accept:
                    case HttpConstants.HttpHeaders.ContentType:
                    case HttpConstants.HttpHeaders.Host:
                    case HttpConstants.HttpHeaders.IfMatch:
                    case HttpConstants.HttpHeaders.IfModifiedSince:
                    case HttpConstants.HttpHeaders.IfNoneMatch:
                    case HttpConstants.HttpHeaders.IfRange:
                    case HttpConstants.HttpHeaders.IfUnmodifiedSince:
                    case HttpConstants.HttpHeaders.UserAgent:
                    case HttpConstants.HttpHeaders.Prefer:
                    case HttpConstants.HttpHeaders.Query:
                    case HttpConstants.HttpHeaders.A_IM:
                        return true;

                    default:
                        return false;
                }
            }
            return true;
        }

        private static async Task<Stream> BufferContentIfAvailableAsync(HttpResponseMessage responseMessage)
        {
            if (responseMessage.Content == null)
            {
                return null;
            }

            MemoryStream bufferedStream = new MemoryStream();
            await responseMessage.Content.CopyToAsync(bufferedStream);
            bufferedStream.Position = 0;
            return bufferedStream;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposable object returned by method")]
        private async Task<HttpRequestMessage> PrepareRequestMessageAsync(
            DocumentServiceRequest request,
            Uri physicalAddress)
        {
            HttpMethod httpMethod = HttpMethod.Head;
            if (request.OperationType == OperationType.Create ||
                request.OperationType == OperationType.Upsert ||
                request.OperationType == OperationType.Query ||
                request.OperationType == OperationType.SqlQuery ||
                request.OperationType == OperationType.Batch ||
                request.OperationType == OperationType.ExecuteJavaScript ||
                request.OperationType == OperationType.QueryPlan)
            {
                httpMethod = HttpMethod.Post;
            }
            else if (request.OperationType == OperationType.Read
                || request.OperationType == OperationType.ReadFeed)
            {
                httpMethod = HttpMethod.Get;
            }
            else if (request.OperationType == OperationType.Replace)
            {
                httpMethod = HttpMethod.Put;
            }
            else if (request.OperationType == OperationType.Delete)
            {
                httpMethod = HttpMethod.Delete;
            }
            else
            {
                throw new NotImplementedException();
            }

            HttpRequestMessage requestMessage = new HttpRequestMessage(httpMethod, physicalAddress);

            // The StreamContent created below will own and dispose its underlying stream, but we may need to reuse the stream on the 
            // DocumentServiceRequest for future requests. Hence we need to clone without incurring copy cost, so that when
            // HttpRequestMessage -> StreamContent -> MemoryStream all get disposed, the original stream will be left open.
            if (request.Body != null)
            {
                await request.EnsureBufferedBodyAsync();
                MemoryStream clonedStream = new MemoryStream();
                // WriteTo doesn't use and update Position of source stream. No point in setting/restoring it.
                request.CloneableBody.WriteTo(clonedStream);
                clonedStream.Position = 0;

                requestMessage.Content = new StreamContent(clonedStream);
            }

            if (request.Headers != null)
            {
                foreach (string key in request.Headers)
                {
                    if (GatewayStoreClient.IsAllowedRequestHeader(key))
                    {
                        if (key.Equals(HttpConstants.HttpHeaders.ContentType, StringComparison.OrdinalIgnoreCase))
                        {
                            requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(request.Headers[key]);
                        }
                        else
                        {
                            requestMessage.Headers.TryAddWithoutValidation(key, request.Headers[key]);
                        }
                    }
                }
            }

            // add activityId
            Guid activityId = Trace.CorrelationManager.ActivityId;
            Debug.Assert(activityId != Guid.Empty);
            requestMessage.Headers.Add(HttpConstants.HttpHeaders.ActivityId, activityId.ToString());

            return requestMessage;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposable object returned by method")]
        private async Task<HttpResponseMessage> InvokeClientAsync(
           DocumentServiceRequest request,
           ResourceType resourceType,
           Uri physicalAddress,
           CancellationToken cancellationToken)
        {
            Func<Task<HttpResponseMessage>> funcDelegate = async () =>
            {
                using (HttpRequestMessage requestMessage = await this.PrepareRequestMessageAsync(request, physicalAddress))
                {
                    DateTime sendTimeUtc = DateTime.UtcNow;
                    Guid localGuid = Guid.NewGuid();  // For correlating HttpRequest and HttpResponse Traces

                    IClientSideRequestStatistics clientSideRequestStatistics = request.RequestContext.ClientRequestStatistics;
                    if (clientSideRequestStatistics == null)
                    {
                        clientSideRequestStatistics = new CosmosClientSideRequestStatistics();
                        request.RequestContext.ClientRequestStatistics = clientSideRequestStatistics;
                    }

                    Guid requestedActivityId = Trace.CorrelationManager.ActivityId;
                    this.eventSource.Request(
                        requestedActivityId,
                        localGuid,
                        requestMessage.RequestUri.ToString(),
                        resourceType.ToResourceTypeString(),
                        requestMessage.Headers);

                    TimeSpan durationTimeSpan;
                    string recordAddressResolutionId = clientSideRequestStatistics.RecordAddressResolutionStart(requestMessage.RequestUri);
                    try
                    {
                        HttpResponseMessage responseMessage = await this.httpClient.SendAsync(requestMessage, cancellationToken);

                        DateTime receivedTimeUtc = DateTime.UtcNow;
                        durationTimeSpan = receivedTimeUtc - sendTimeUtc;

                        IEnumerable<string> headerValues;
                        Guid activityId = Guid.Empty;
                        if (responseMessage.Headers.TryGetValues(HttpConstants.HttpHeaders.ActivityId, out headerValues) &&
                            headerValues.Count() != 0)
                        {
                            activityId = new Guid(headerValues.First());
                        }

                        this.eventSource.Response(
                            activityId,
                            localGuid,
                            (short)responseMessage.StatusCode,
                            durationTimeSpan.TotalMilliseconds,
                            responseMessage.Headers);

                        return responseMessage;
                    }
                    catch (TaskCanceledException ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            // throw timeout if the cancellationToken is not canceled (i.e. httpClient timed out)
                            durationTimeSpan = DateTime.UtcNow - sendTimeUtc;
                            string message = $"GatewayStoreClient Request Timeout. Start Time:{sendTimeUtc}; Total Duration:{durationTimeSpan}; Http Client Timeout:{this.httpClient.Timeout}; Activity id: {requestedActivityId}; Inner Message: {ex.Message};";
                            throw new RequestTimeoutException(message, ex, requestMessage.RequestUri);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            // throw timeout if the cancellationToken is not canceled (i.e. httpClient timed out)
                            durationTimeSpan = DateTime.UtcNow - sendTimeUtc;
                            string message = $"GatewayStoreClient Request Timeout. Start Time:{sendTimeUtc}; Total Duration:{durationTimeSpan}; Http Client Timeout:{this.httpClient.Timeout}; Activity id: {requestedActivityId}; Inner Message: {ex.Message};";
                            throw new RequestTimeoutException(message, ex, requestMessage.RequestUri);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    finally
                    {
                        clientSideRequestStatistics.RecordAddressResolutionEnd(recordAddressResolutionId);
                    }
                }
            };

            return await BackoffRetryUtility<HttpResponseMessage>.ExecuteAsync(funcDelegate, new WebExceptionRetryPolicy(), cancellationToken);
        }
    }
}
