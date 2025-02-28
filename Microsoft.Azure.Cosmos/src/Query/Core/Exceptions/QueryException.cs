﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Query.Core
{
    using System;

    internal abstract class QueryException : Exception
    {
        protected QueryException()
            : base()
        {
        }

        protected QueryException(string message)
            : base(message)
        {
        }

        protected QueryException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public abstract TResult Accept<TResult>(QueryExceptionVisitor<TResult> visitor);
    }
}
