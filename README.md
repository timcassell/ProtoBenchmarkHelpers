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

    private readonly Action interlockedAction;
    private readonly Action lockedAction;

    public ThreadBenchmarks()
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

    [GlobalSetup(Target = nameof(ThreadHelperInterlocked))]
    public void SetupInterlocked()
    {
        threadHelper = new BenchmarkThreadHelper();
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
    }

    [Benchmark]
    public void ThreadHelperInterlocked()
    {
        threadHelper.ExecuteAndWait();
    }

    [GlobalSetup(Target = nameof(ThreadHelperLocked))]
    public void SetupLocked()
    {
        threadHelper = new BenchmarkThreadHelper();
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
    }

    [Benchmark]
    public void ThreadHelperLocked()
    {
        threadHelper.ExecuteAndWait();
    }
}
```

Compare to `System.Threading.Tasks.Parallel`:

|                  Method | MaxConcurrency |       Mean |    Error |   StdDev | Allocated |
|------------------------ |--------------- |-----------:|---------:|---------:|----------:|
|        ParallelOverhead |             -1 | 4,063.6 ns | 72.60 ns | 67.91 ns |     567 B |
|     ParallelInterlocked |             -1 | 4,364.6 ns | 45.91 ns | 42.95 ns |     575 B |
|          ParallelLocked |             -1 | 4,679.3 ns | 47.28 ns | 44.23 ns |     580 B |
|    ThreadHelperOverhead |             -1 | 1,125.2 ns |  2.35 ns |  1.96 ns |         - |
| ThreadHelperInterlocked |             -1 | 1,144.2 ns |  2.06 ns |  1.72 ns |         - |
|      ThreadHelperLocked |             -1 | 1,667.7 ns |  5.93 ns |  5.54 ns |         - |
|        ParallelOverhead |              2 | 2,784.2 ns | 13.42 ns | 12.56 ns |    1344 B |
|     ParallelInterlocked |              2 | 2,771.0 ns | 17.82 ns | 16.67 ns |    1344 B |
|          ParallelLocked |              2 | 2,744.3 ns | 12.67 ns | 11.85 ns |    1344 B |
|    ThreadHelperOverhead |              2 |   825.2 ns |  3.67 ns |  3.44 ns |         - |
| ThreadHelperInterlocked |              2 |   902.3 ns |  7.13 ns |  6.67 ns |         - |
|      ThreadHelperLocked |              2 | 1,229.7 ns |  3.20 ns |  2.84 ns |         - |

<details><summary>Benchmark Code</summary>
<p>

```cs
class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<ThreadBenchmarks>();
    }
}

[MemoryDiagnoser(false)]
public class ThreadBenchmarks
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

    public ThreadBenchmarks()
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


    [GlobalSetup(Targets = new[] { nameof(ParallelOverhead), nameof(ParallelInterlocked), nameof(ParallelLocked) })]
    public void SetupParallelOptions()
    {
        parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = MaxConcurrency };
    }

    [Benchmark]
    public void ParallelOverhead()
    {
        Parallel.Invoke(parallelOptions,
            overheadAction,
            overheadAction,
            overheadAction,
            overheadAction);
    }

    [Benchmark]
    public void ParallelInterlocked()
    {
        Parallel.Invoke(parallelOptions,
            interlockedAction,
            interlockedAction,
            interlockedAction,
            interlockedAction);
    }

    [Benchmark]
    public void ParallelLocked()
    {
        Parallel.Invoke(parallelOptions,
            lockedAction,
            lockedAction,
            lockedAction,
            lockedAction);
    }

    [GlobalSetup(Target = nameof(ThreadHelperOverhead))]
    public void SetupOverhead()
    {
        threadHelper = new BenchmarkThreadHelper(MaxConcurrency);
        threadHelper.AddAction(overheadAction);
        threadHelper.AddAction(overheadAction);
        threadHelper.AddAction(overheadAction);
        threadHelper.AddAction(overheadAction);
    }

    [Benchmark]
    public void ThreadHelperOverhead()
    {
        threadHelper.ExecuteAndWait();
    }

    [GlobalSetup(Target = nameof(ThreadHelperInterlocked))]
    public void SetupInterlocked()
    {
        threadHelper = new BenchmarkThreadHelper(MaxConcurrency);
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
        threadHelper.AddAction(interlockedAction);
    }

    [Benchmark]
    public void ThreadHelperInterlocked()
    {
        threadHelper.ExecuteAndWait();
    }

    [GlobalSetup(Target = nameof(ThreadHelperLocked))]
    public void SetupLocked()
    {
        threadHelper = new BenchmarkThreadHelper(MaxConcurrency);
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
        threadHelper.AddAction(lockedAction);
    }

    [Benchmark]
    public void ThreadHelperLocked()
    {
        threadHelper.ExecuteAndWait();
    }
}
```

</p>
</details>

