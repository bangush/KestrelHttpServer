// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using BenchmarkDotNet.Running;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //var bm = new ResponseHeadersWritingBenchmark();
            //bm.Type = ResponseHeadersWritingBenchmark.BenchmarkTypes.TechEmpowerPlaintext;
            //bm.Setup();
            //MainAsync(bm).Wait();
            BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
        }

        private async static Task MainAsync(ResponseHeadersWritingBenchmark bm)
        {
            for(var i= 0; i< 100000000; i++)
            {
                await bm.Output();
            }
        }
    }
}
