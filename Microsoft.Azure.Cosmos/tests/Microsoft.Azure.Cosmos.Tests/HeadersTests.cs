﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class HeadersTests
    {
        private const string Key = "testKey";

        [TestMethod]
        public void TestAddAndGetAndSet()
        {
            string value1 = Guid.NewGuid().ToString();
            string value2 = Guid.NewGuid().ToString();
            Headers Headers = new Headers();
            Headers.Add(Key, value1);
            Assert.AreEqual(value1, Headers.Get(Key));
            Headers.Set(Key, value2);
            Assert.AreEqual(value2, Headers.Get(Key));
        }

        [TestMethod]
        public void TestIndexer()
        {
            Headers Headers = new Headers();
            string value = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[Key] = value;
            Assert.AreEqual(value, Headers[Key]);
        }

        [TestMethod]
        public void TestRemove()
        {
            Headers Headers = new Headers();
            string value = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[Key] = value;
            Assert.AreEqual(value, Headers[Key]);
            Headers.Remove(Key);
            Assert.IsNull(Headers[Key]);
        }

        [TestMethod]
        public void TestClear()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders.Clear();
            Assert.IsNull(Headers[Key]);
        }

        [TestMethod]
        public void TestCount()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Assert.AreEqual(1, Headers.CosmosMessageHeaders.Count());
        }

        [TestMethod]
        public void TestGetValues()
        {
            Headers Headers = new Headers();
            string value1 = Guid.NewGuid().ToString();
            Headers.Add(Key, value1);
            IEnumerable<string> values = Headers.GetValues(Key);
            Assert.AreEqual(1, values.Count());
        }

        [TestMethod]
        public void TestAllKeys()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Assert.AreEqual(Key, Headers.AllKeys().First());
        }

        [TestMethod]
        public void TestGetIEnumerableKeys()
        {
            Headers Headers = new Headers();
            string value = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[Key] = value;
            foreach (string header in Headers)
            {
                Assert.AreEqual(value, Headers[header]);
                return;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(NotImplementedException))]
        public void TestGetToNameValueCollection()
        {
            Headers Headers = new Headers();
            string value = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[Key] = value;
            NameValueCollection anotherCollection = Headers.CosmosMessageHeaders.ToNameValueCollection();
            Assert.AreEqual(value, anotherCollection[Key]);
        }

        [TestMethod]
        public void TestSetAndGetKnownProperties()
        {
            // Most commonly used in the Request
            {
                string value1 = Guid.NewGuid().ToString();
                string value2 = Guid.NewGuid().ToString();
                string value3 = Guid.NewGuid().ToString();
                string value4 = Guid.NewGuid().ToString();
                Headers Headers = new Headers();
                Headers.ContinuationToken = value1;
                Headers.PartitionKey = value2;
                Headers.PartitionKeyRangeId = value3;
                Assert.AreEqual(value1, Headers.ContinuationToken);
                Assert.AreEqual(value2, Headers.PartitionKey);
                Assert.AreEqual(value3, Headers.PartitionKeyRangeId);
                Assert.AreEqual(value1, Headers[HttpConstants.HttpHeaders.Continuation]);
                Assert.AreEqual(value2, Headers[HttpConstants.HttpHeaders.PartitionKey]);
                Assert.AreEqual(value3, Headers[WFConstants.BackendHeaders.PartitionKeyRangeId]);
                value1 = Guid.NewGuid().ToString();
                value2 = Guid.NewGuid().ToString();
                value3 = Guid.NewGuid().ToString();
                Headers.CosmosMessageHeaders[HttpConstants.HttpHeaders.Continuation] = value1;
                Headers.CosmosMessageHeaders[HttpConstants.HttpHeaders.PartitionKey] = value2;
                Headers.CosmosMessageHeaders[WFConstants.BackendHeaders.PartitionKeyRangeId] = value3;
                Assert.AreEqual(value1, Headers.ContinuationToken);
                Assert.AreEqual(value2, Headers.PartitionKey);
                Assert.AreEqual(value3, Headers.PartitionKeyRangeId);
                Assert.AreEqual(value1, Headers[HttpConstants.HttpHeaders.Continuation]);
                Assert.AreEqual(value2, Headers[HttpConstants.HttpHeaders.PartitionKey]);
                Assert.AreEqual(value3, Headers[WFConstants.BackendHeaders.PartitionKeyRangeId]);
            }

            // Most commonly used in the Response
            {
                string value1 = Guid.NewGuid().ToString();
                string value2 = "1002";
                string value3 = "20";
                string value4 = "someSession";
                Headers requestHeaders = new Headers();
                requestHeaders.CosmosMessageHeaders[HttpConstants.HttpHeaders.Continuation] = value1;
                requestHeaders.CosmosMessageHeaders[WFConstants.BackendHeaders.SubStatus] = value2;
                requestHeaders.CosmosMessageHeaders[HttpConstants.HttpHeaders.RetryAfterInMilliseconds] = value3;
                requestHeaders.CosmosMessageHeaders[HttpConstants.HttpHeaders.SessionToken] = value4;
                Assert.AreEqual(value1, requestHeaders.ContinuationToken);
                Assert.AreEqual(int.Parse(value2), (int)requestHeaders.SubStatusCode);
                Assert.AreEqual(TimeSpan.FromMilliseconds(20), requestHeaders.RetryAfter);
                Assert.AreEqual(value4, requestHeaders.Session);
                Assert.AreEqual(value1, requestHeaders[HttpConstants.HttpHeaders.Continuation]);
                Assert.AreEqual(value2, requestHeaders[WFConstants.BackendHeaders.SubStatus]);
                Assert.AreEqual(value3, requestHeaders[HttpConstants.HttpHeaders.RetryAfterInMilliseconds]);
                Assert.AreEqual(value4, requestHeaders[HttpConstants.HttpHeaders.SessionToken]);
            }
        }

        [TestMethod]
        public void TestClearWithKnownProperties()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Headers.PartitionKey = Guid.NewGuid().ToString();
            Headers.ContinuationToken = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[HttpConstants.HttpHeaders.RetryAfterInMilliseconds] = "20";
            Headers.CosmosMessageHeaders.Clear();
            Assert.IsNull(Headers[Key]);
            Assert.IsNull(Headers.PartitionKey);
            Assert.IsNull(Headers.ContinuationToken);
            Assert.IsNull(Headers.RetryAfter);
        }

        [TestMethod]
        public void TestCountWithKnownProperties()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Headers.PartitionKey = Guid.NewGuid().ToString();
            Headers.ContinuationToken = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[HttpConstants.HttpHeaders.RetryAfterInMilliseconds] = "20";
            Assert.AreEqual(4, Headers.CosmosMessageHeaders.Count());
        }

        [TestMethod]
        public void TestAllKeysWithKnownProperties()
        {
            Headers Headers = new Headers();
            Headers.CosmosMessageHeaders[Key] = Guid.NewGuid().ToString();
            Headers.ContinuationToken = Guid.NewGuid().ToString();
            Headers.CosmosMessageHeaders[HttpConstants.HttpHeaders.RetryAfterInMilliseconds] = "20";
            Headers.Add(WFConstants.BackendHeaders.SubStatus, "1002");
            Headers.PartitionKey = Guid.NewGuid().ToString();
            string[] allKeys = Headers.AllKeys();
            Assert.IsTrue(allKeys.Contains(Key));
            Assert.IsTrue(allKeys.Contains(HttpConstants.HttpHeaders.PartitionKey));
            Assert.IsTrue(allKeys.Contains(HttpConstants.HttpHeaders.RetryAfterInMilliseconds));
            Assert.IsTrue(allKeys.Contains(HttpConstants.HttpHeaders.Continuation));
            Assert.IsTrue(allKeys.Contains(WFConstants.BackendHeaders.SubStatus));
        }

        [TestMethod]
        public void AllKnownPropertiesHaveGetAndSetAndIndexed()
        {
            Headers Headers = new Headers();
            IEnumerable<PropertyInfo> knownHeaderProperties = typeof(Headers)
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttributes(typeof(CosmosKnownHeaderAttribute), false).Any());

            foreach (PropertyInfo knownHeaderProperty in knownHeaderProperties)
            {
                string value = "123456789";
                string header = ((CosmosKnownHeaderAttribute)knownHeaderProperty.GetCustomAttributes(typeof(CosmosKnownHeaderAttribute), false).First()).HeaderName;
                Headers.CosmosMessageHeaders[header] = value; // Using indexer

                Assert.AreEqual(value, (string)knownHeaderProperty.GetValue(Headers)); // Verify getter

                value = "987654321";
                knownHeaderProperty.SetValue(Headers, value);
                Assert.AreEqual(value, Headers[header]);
            }
        }
    }
}
