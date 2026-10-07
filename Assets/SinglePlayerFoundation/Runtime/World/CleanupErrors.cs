using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SPF.Runtime.World
{
    /// <summary>Cold lifecycle cleanup: retain the first failure and attempt all remaining owners.</summary>
    public static class CleanupErrors
    {
        /// <summary>Secondary failures are an AggregateException on the original exception's Data.</summary>
        public const string DataKey = "SPF.CleanupFailures";

        public static void Try(Action cleanup, ref Exception failure)
        {
            try { cleanup(); }
            catch (Exception error)
            {
                if (failure == null) failure = error;
                else if (!ReferenceEquals(failure, error))
                {
                    var errors = new List<Exception>();
                    if (failure.Data[DataKey] is AggregateException previous)
                        errors.AddRange(previous.InnerExceptions);
                    errors.Add(error);
                    if (error.Data[DataKey] is AggregateException nested)
                        errors.AddRange(nested.InnerExceptions);
                    failure.Data[DataKey] = new AggregateException("Additional lifecycle cleanup failures.", errors);
                }
            }
        }

        public static void ThrowIfAny(Exception failure)
        {
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
