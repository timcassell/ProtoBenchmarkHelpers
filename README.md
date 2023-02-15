# ProtoBenchmarkHelpers
Helpers for [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet).

## BenchmarkThreadHelper

Useful for benchmarking actions ran on multiple threads concurrently more accurately than other solutions.

It can be the same action ran multiple times in parallel, like incrementing a counter. Or it can be different actions ran at the same time in parallel, like reading from and writing to a concurrent collection.

Example, to compare the performance of incrementing a counter using Interlocked vs with a lock:

```cs
public class ThreadBenchmarks
{
    private BenchmarkThreadHelper threadHelper;

    private readonly object locker = new();
    private int counter;

    [GlobalCleanup]
    public void Cleanup()
    {
        threadHelper.Dispose();
    }

    [GlobalSetup(Target = nameof(ThreadHelperInterlocked))]
    public void SetupInterlocked()
    {
        Action action = () => Interlocked.Increment(ref counter);
        threadHelper = new BenchmarkThreadHelper()
        {
            action,
            action,
            action,
            action
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
        Action action = () =>
        {
            unchecked
            {
                lock (locker)
                {
                    ++counter;
                }
            }
        };
        threadHelper = new BenchmarkThreadHelper()
        {
            action,
            action,
            action,
            action
        };
    }

    [Benchmark]
    public void ThreadHelperLocked()
    {
        threadHelper.ExecuteAndWait();
    }
}
```

Compare to `System.Threading.Tasks.Parallel`:

|                  Method | MaxConcurrency |       Mean |    Error |    StdDev | Allocated |
|------------------------ |--------------- |-----------:|---------:|----------:|----------:|
|        ParallelOverhead |             -1 | 4,086.4 ns | 78.81 ns |  80.93 ns |     514 B |
|     ParallelInterlocked |             -1 | 4,370.0 ns | 86.95 ns | 147.65 ns |     517 B |
|          ParallelLocked |             -1 | 4,798.6 ns | 54.14 ns |  50.64 ns |     522 B |
|    ThreadHelperOverhead |             -1 | 1,172.7 ns |  3.55 ns |   3.32 ns |         - |
| ThreadHelperInterlocked |             -1 | 1,302.8 ns |  2.07 ns |   1.83 ns |         - |
|      ThreadHelperLocked |             -1 | 1,660.2 ns |  4.42 ns |   4.13 ns |         - |
|        ParallelOverhead |              2 | 2,843.1 ns | 19.03 ns |  17.80 ns |    1288 B |
|     ParallelInterlocked |              2 | 2,737.6 ns | 20.99 ns |  19.63 ns |    1288 B |
|          ParallelLocked |              2 | 2,805.0 ns | 18.68 ns |  17.48 ns |    1288 B |
|    ThreadHelperOverhead |              2 |   602.1 ns |  2.39 ns |   2.23 ns |         - |
| ThreadHelperInterlocked |              2 |   697.4 ns |  2.77 ns |   2.59 ns |         - |
|      ThreadHelperLocked |              2 |   992.7 ns |  2.93 ns |   2.74 ns |         - |