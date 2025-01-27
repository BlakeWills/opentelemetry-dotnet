// <copyright file="MetricReader.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System;
using System.Diagnostics;
using System.Threading;

namespace OpenTelemetry.Metrics
{
    public abstract class MetricReader : IDisposable
    {
        private const AggregationTemporality CumulativeAndDelta = AggregationTemporality.Cumulative | AggregationTemporality.Delta;
        private AggregationTemporality preferredAggregationTemporality = CumulativeAndDelta;
        private AggregationTemporality supportedAggregationTemporality = CumulativeAndDelta;
        private int shutdownCount;

        public BaseProvider ParentProvider { get; private set; }

        public AggregationTemporality PreferredAggregationTemporality
        {
            get => this.preferredAggregationTemporality;
            set
            {
                ValidateAggregationTemporality(value, this.supportedAggregationTemporality);
                this.preferredAggregationTemporality = value;
            }
        }

        public AggregationTemporality SupportedAggregationTemporality
        {
            get => this.supportedAggregationTemporality;
            set
            {
                ValidateAggregationTemporality(this.preferredAggregationTemporality, value);
                this.supportedAggregationTemporality = value;
            }
        }

        /// <summary>
        /// Attempts to collect the metrics, blocks the current thread until
        /// metrics collection completed or timed out.
        /// </summary>
        /// <param name="timeoutMilliseconds">
        /// The number (non-negative) of milliseconds to wait, or
        /// <c>Timeout.Infinite</c> to wait indefinitely.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> when metrics collection succeeded; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the <c>timeoutMilliseconds</c> is smaller than -1.
        /// </exception>
        /// <remarks>
        /// This function guarantees thread-safety.
        /// </remarks>
        public bool Collect(int timeoutMilliseconds = Timeout.Infinite)
        {
            if (timeoutMilliseconds < 0 && timeoutMilliseconds != Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "timeoutMilliseconds should be non-negative or Timeout.Infinite.");
            }

            try
            {
                return this.OnCollect(timeoutMilliseconds);
            }
            catch (Exception)
            {
                // TODO: OpenTelemetrySdkEventSource.Log.SpanProcessorException(nameof(this.Collect), ex);
                return false;
            }
        }

        /// <summary>
        /// Attempts to shutdown the processor, blocks the current thread until
        /// shutdown completed or timed out.
        /// </summary>
        /// <param name="timeoutMilliseconds">
        /// The number (non-negative) of milliseconds to wait, or
        /// <c>Timeout.Infinite</c> to wait indefinitely.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> when shutdown succeeded; otherwise, <c>false</c>.
        /// </returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the <c>timeoutMilliseconds</c> is smaller than -1.
        /// </exception>
        /// <remarks>
        /// This function guarantees thread-safety. Only the first call will
        /// win, subsequent calls will be no-op.
        /// </remarks>
        public bool Shutdown(int timeoutMilliseconds = Timeout.Infinite)
        {
            if (timeoutMilliseconds < 0 && timeoutMilliseconds != Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "timeoutMilliseconds should be non-negative or Timeout.Infinite.");
            }

            if (Interlocked.Increment(ref this.shutdownCount) > 1)
            {
                return false; // shutdown already called
            }

            try
            {
                return this.OnShutdown(timeoutMilliseconds);
            }
            catch (Exception)
            {
                // TODO: OpenTelemetrySdkEventSource.Log.SpanProcessorException(nameof(this.Shutdown), ex);
                return false;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal virtual void SetParentProvider(BaseProvider parentProvider)
        {
            this.ParentProvider = parentProvider;
        }

        /// <summary>
        /// Processes a batch of metrics.
        /// </summary>
        /// <param name="metrics">Batch of metrics to be processed.</param>
        /// <param name="timeoutMilliseconds">
        /// The number (non-negative) of milliseconds to wait, or
        /// <c>Timeout.Infinite</c> to wait indefinitely.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> when metrics processing succeeded; otherwise,
        /// <c>false</c>.
        /// </returns>
        protected abstract bool ProcessMetrics(Batch<Metric> metrics, int timeoutMilliseconds);

        /// <summary>
        /// Called by <c>Collect</c>. This function should block the current
        /// thread until metrics collection completed, shutdown signaled or
        /// timed out.
        /// </summary>
        /// <param name="timeoutMilliseconds">
        /// The number (non-negative) of milliseconds to wait, or
        /// <c>Timeout.Infinite</c> to wait indefinitely.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> when metrics collection succeeded; otherwise,
        /// <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This function is called synchronously on the thread which called
        /// <c>Collect</c>. This function should be thread-safe, and should
        /// not throw exceptions.
        /// </remarks>
        protected virtual bool OnCollect(int timeoutMilliseconds)
        {
            var sw = Stopwatch.StartNew();

            var collectMetric = this.ParentProvider.GetMetricCollect();
            var metrics = collectMetric();

            if (timeoutMilliseconds == Timeout.Infinite)
            {
                return this.ProcessMetrics(metrics, Timeout.Infinite);
            }
            else
            {
                var timeout = timeoutMilliseconds - sw.ElapsedMilliseconds;

                if (timeout <= 0)
                {
                    return false;
                }

                return this.ProcessMetrics(metrics, (int)timeout);
            }
        }

        /// <summary>
        /// Called by <c>Shutdown</c>. This function should block the current
        /// thread until shutdown completed or timed out.
        /// </summary>
        /// <param name="timeoutMilliseconds">
        /// The number (non-negative) of milliseconds to wait, or
        /// <c>Timeout.Infinite</c> to wait indefinitely.
        /// </param>
        /// <returns>
        /// Returns <c>true</c> when shutdown succeeded; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// This function is called synchronously on the thread which made the
        /// first call to <c>Shutdown</c>. This function should not throw
        /// exceptions.
        /// </remarks>
        protected virtual bool OnShutdown(int timeoutMilliseconds)
        {
            return this.Collect(timeoutMilliseconds);
        }

        /// <summary>
        /// Releases the unmanaged resources used by this class and optionally
        /// releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true"/> to release both managed and unmanaged resources;
        /// <see langword="false"/> to release only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
        }

        private static void ValidateAggregationTemporality(AggregationTemporality preferred, AggregationTemporality supported)
        {
            if ((int)(preferred & CumulativeAndDelta) == 0)
            {
                throw new ArgumentException($"PreferredAggregationTemporality has an invalid value {preferred}.", nameof(preferred));
            }

            if ((int)(supported & CumulativeAndDelta) == 0)
            {
                throw new ArgumentException($"SupportedAggregationTemporality has an invalid value {supported}.", nameof(supported));
            }

            /*
            | Preferred  | Supported  | Valid |
            | ---------- | ---------- | ----- |
            | Both       | Both       | true  |
            | Both       | Cumulative | false |
            | Both       | Delta      | false |
            | Cumulative | Both       | true  |
            | Cumulative | Cumulative | true  |
            | Cumulative | Delta      | false |
            | Delta      | Both       | true  |
            | Delta      | Cumulative | false |
            | Delta      | Delta      | true  |
            */
            if ((int)(preferred & supported) == 0 || preferred > supported)
            {
                throw new ArgumentException($"PreferredAggregationTemporality {preferred} and SupportedAggregationTemporality {supported} are incompatible.");
            }
        }
    }
}
