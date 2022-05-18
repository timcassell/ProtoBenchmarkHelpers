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

|                  Method | MaxConcurrency |     Mean |     Error |    StdDev | Allocated |
|------------------------ |--------------- |---------:|----------:|----------:|----------:|
|       ParallellOverhead |             -1 | 3.817 us | 0.0755 us | 0.0741 us |     575 B |
|    ParallellInterlocked |             -1 | 4.001 us | 0.0792 us | 0.1029 us |     576 B |
|         ParallellLocked |             -1 | 4.348 us | 0.0414 us | 0.0387 us |     587 B |
|    ThreadHelperOverhead |             -1 | 1.245 us | 0.0084 us | 0.0070 us |         - |
| ThreadHelperInterlocked |             -1 | 1.363 us | 0.0089 us | 0.0079 us |         - |
|      ThreadHelperLocked |             -1 | 2.340 us | 0.0176 us | 0.0164 us |         - |
|       ParallellOverhead |              2 | 2.625 us | 0.0468 us | 0.0501 us |   1,344 B |
|    ParallellInterlocked |              2 | 2.539 us | 0.0226 us | 0.0212 us |   1,344 B |
|         ParallellLocked |              2 | 2.548 us | 0.0183 us | 0.0171 us |   1,344 B |
|    ThreadHelperOverhead |              2 | 1.214 us | 0.0088 us | 0.0078 us |         - |
| ThreadHelperInterlocked |              2 | 1.354 us | 0.0077 us | 0.0072 us |         - |
|      ThreadHelperLocked |              2 | 2.314 us | 0.0306 us | 0.0314 us |         - |

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


    [GlobalSetup(Targets = new[] { nameof(ParallellOverhead), nameof(ParallellInterlocked), nameof(ParallellLocked) })]
    public void SetupParallelOptions()
    {
        parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = MaxConcurrency };
    }

    [Benchmark]
    public void ParallellOverhead()
    {
        Parallel.Invoke(parallelOptions,
            overheadAction,
            overheadAction,
            overheadAction,
            overheadAction);
    }

    [Benchmark]
    public void ParallellInterlocked()
    {
        Parallel.Invoke(parallelOptions,
            interlockedAction,
            interlockedAction,
            interlockedAction,
            interlockedAction);
    }

    [Benchmark]
    public void ParallellLocked()
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
        threadHelper = new BenchmarkThreadHelper();
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

</p>
</details>

