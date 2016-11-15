// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class CoreConfig : ManualConfig
    {
        public CoreConfig()
        {
            Add(Job.Default.
                With(Platform.X64).
                With(Jit.RyuJit).
                With(BenchmarkDotNet.Environments.Runtime.Core).
                WithRemoveOutliers(true).
                With(new GcMode() { Server = true }).
                With(RunStrategy.Throughput).
                WithLaunchCount(3).
                WithWarmupCount(5).
                WithTargetCount(10));
        }
    }
}
