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
|        ParallelOverhead |             -1 | 4.222 us | 0.0625 us | 0.0585 us |     573 B |
|     ParallelInterlocked |             -1 | 4.328 us | 0.0734 us | 0.0686 us |     579 B |
|          ParallelLocked |             -1 | 4.658 us | 0.0303 us | 0.0284 us |     585 B |
|    ThreadHelperOverhead |             -1 | 1.010 us | 0.0030 us | 0.0027 us |         - |
| ThreadHelperInterlocked |             -1 | 1.144 us | 0.0051 us | 0.0048 us |         - |
|      ThreadHelperLocked |             -1 | 1.746 us | 0.0041 us | 0.0034 us |         - |
|        ParallelOverhead |              2 | 2.607 us | 0.0364 us | 0.0304 us |   1,344 B |
|     ParallelInterlocked |              2 | 2.669 us | 0.0218 us | 0.0182 us |   1,344 B |
|          ParallelLocked |              2 | 2.685 us | 0.0466 us | 0.0436 us |   1,344 B |
|    ThreadHelperOverhead |              2 | 1.012 us | 0.0064 us | 0.0050 us |         - |
| ThreadHelperInterlocked |              2 | 1.180 us | 0.0033 us | 0.0028 us |         - |
|      ThreadHelperLocked |              2 | 1.647 us | 0.0084 us | 0.0079 us |         - |

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

