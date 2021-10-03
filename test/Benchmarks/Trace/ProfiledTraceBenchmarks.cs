// <copyright file="ProfiledTraceBenchmarks.cs" company="OpenTelemetry Authors">
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

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Benchmarks.Trace
{
    [MemoryDiagnoser]
    [EtwProfiler]
    public class ProfiledTraceBenchmarks
    {
        public ProfiledTraceBenchmarks()
        {
            Activity.DefaultIdFormat = ActivityIdFormat.W3C;

            Sdk.CreateTracerProviderBuilder()
               .SetSampler(new AlwaysOnSampler())
               .AddLegacySource("ExactMatch.OperationName1")
               .AddProcessor(new DummyActivityProcessor())
               .Build();

            // Sdk.CreateTracerProviderBuilder()
            //    .SetSampler(new AlwaysOnSampler())
            //    .AddLegacySource("WildcardMatch.*")
            //    .AddProcessor(new DummyActivityProcessor())
            //    .Build();
        }

        [Benchmark]
        public void LegacyDiagnosticActivity_ExactMatchMode()
        {
            using (var activity = new Activity("ExactMatch.OperationName1"))
            {
            }
        }

        internal class DummyActivityProcessor : BaseProcessor<Activity>
        {
        }
    }
}
