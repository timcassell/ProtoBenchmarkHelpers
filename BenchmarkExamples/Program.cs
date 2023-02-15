using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Proto.Utilities.Benchmark;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkExamples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<BenchmarkThreadHelper_Vs_Parallel_Example>();
        }
    }

    [MemoryDiagnoser(false)]
    public class BenchmarkThreadHelper_Vs_Parallel_Example
    {
        [Params(-1, 2)]
        public int MaxConcurrency { get; set; }

        private BenchmarkThreadHelper threadHelper;
        private ParallelOptions parallelOptions;

        private readonly object locker = new();
        private int counter;

        private readonly Action interlockedAction;
        private readonly Action lockedAction;
        private readonly Action overheadAction = () => { };
        private Action[] parallelActions;

        public BenchmarkThreadHelper_Vs_Parallel_Example()
        {
            interlockedAction = () =>
            {
                Interlocked.Increment(ref counter);
            };
            lockedAction = () =>
            {
                unchecked
                {
                    lock (locker)
                    {
                        ++counter;
                    }
                }
            };
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            threadHelper?.Dispose();
        }


        [GlobalSetup(Target = nameof(ParallelOverhead))]
        public void SetupParallelOverhead()
        {
            parallelOptions = new() { MaxDegreeOfParallelism = MaxConcurrency };
            parallelActions = new[]
            {
                overheadAction,
                overheadAction,
                overheadAction,
                overheadAction
            };
        }

        [Benchmark]
        public void ParallelOverhead()
        {
            Parallel.Invoke(parallelOptions, parallelActions);
        }

        [GlobalSetup(Target = nameof(ParallelInterlocked))]
        public void SetupParallelInterlocked()
        {
            parallelOptions = new() { MaxDegreeOfParallelism = MaxConcurrency };
            parallelActions = new[]
            {
                interlockedAction,
                interlockedAction,
                interlockedAction,
                interlockedAction
            };
        }

        [Benchmark]
        public void ParallelInterlocked()
        {
            Parallel.Invoke(parallelOptions, parallelActions);
        }

        [GlobalSetup(Target = nameof(ParallelLocked))]
        public void SetupParallelLocked()
        {
            parallelOptions = new() { MaxDegreeOfParallelism = MaxConcurrency };
            parallelActions = new[]
            {
                lockedAction,
                lockedAction,
                lockedAction,
                lockedAction
            };
        }

        [Benchmark]
        public void ParallelLocked()
        {
            Parallel.Invoke(parallelOptions, parallelActions);
        }


        [GlobalSetup(Target = nameof(ThreadHelperOverhead))]
        public void SetupOverhead()
        {
            threadHelper = new(MaxConcurrency)
            {
                overheadAction,
                overheadAction,
                overheadAction,
                overheadAction
            };
        }

        [Benchmark]
        public void ThreadHelperOverhead()
        {
            threadHelper.ExecuteAndWait();
        }

        [GlobalSetup(Target = nameof(ThreadHelperInterlocked))]
        public void SetupInterlocked()
        {
            threadHelper = new(MaxConcurrency)
            {
                interlockedAction,
                interlockedAction,
                interlockedAction,
                interlockedAction
            };
        }

        [Benchmark]
        public void ThreadHelperInterlocked()
        {
            threadHelper.ExecuteAndWait();
        }

        [GlobalSetup(Target = nameof(ThreadHelperLocked))]
        public void SetupLocked()
        {
            threadHelper = new(MaxConcurrency)
            {
                lockedAction,
                lockedAction,
                lockedAction,
                lockedAction
            };
        }

        [Benchmark]
        public void ThreadHelperLocked()
        {
            threadHelper.ExecuteAndWait();
        }
    }
}