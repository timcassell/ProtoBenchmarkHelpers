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
|        ParallelOverhead |             -1 | 3.714 us | 0.0741 us | 0.1015 us |     575 B |
|     ParallelInterlocked |             -1 | 3.969 us | 0.0776 us | 0.0924 us |     578 B |
|          ParallelLocked |             -1 | 4.326 us | 0.0351 us | 0.0293 us |     585 B |
|    ThreadHelperOverhead |             -1 | 1.094 us | 0.0030 us | 0.0028 us |         - |
| ThreadHelperInterlocked |             -1 | 1.189 us | 0.0019 us | 0.0016 us |         - |
|      ThreadHelperLocked |             -1 | 1.822 us | 0.0084 us | 0.0070 us |         - |
|        ParallelOverhead |              2 | 2.504 us | 0.0173 us | 0.0153 us |   1,344 B |
|     ParallelInterlocked |              2 | 2.482 us | 0.0159 us | 0.0133 us |   1,344 B |
|          ParallelLocked |              2 | 2.552 us | 0.0143 us | 0.0127 us |   1,344 B |
|    ThreadHelperOverhead |              2 | 1.108 us | 0.0041 us | 0.0034 us |         - |
| ThreadHelperInterlocked |              2 | 1.140 us | 0.0028 us | 0.0025 us |         - |
|      ThreadHelperLocked |              2 | 1.694 us | 0.0034 us | 0.0032 us |         - |

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

