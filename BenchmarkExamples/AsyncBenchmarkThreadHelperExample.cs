using BenchmarkDotNet.Attributes;
using System;
using System.Threading.Tasks;

namespace Proto.Utilities.Benchmark.Examples
{
    [MemoryDiagnoser(false)]
    public class AsyncBenchmarkThreadHelper_Vs_TaskRun_Example
    {
        private const int numTasks = 2;

        [ParamsAllValues]
        public bool Yield { get; set; }

        private Func<Task> taskAction;
        private readonly Task[] parallelTasks = new Task[numTasks];
        private AsyncBenchmarkThreadHelper asyncThreadHelper;

        private async Task FuncAsync()
        {
            if (Yield)
                await Task.Yield();
        }

        [GlobalSetup(Target = nameof(TaskRun))]
        public void SetupTaskRun()
        {
            taskAction = () => FuncAsync();
        }

        [Benchmark]
        public Task TaskRun()
        {
            for (int i = 0; i < numTasks; ++i)
            {
                parallelTasks[i] = Task.Run(taskAction);
            }
            return Task.WhenAll(parallelTasks);
        }

        [GlobalSetup(Target = nameof(AsyncThreadHelper))]
        public void SetupAsyncThreadHelper()
        {
            asyncThreadHelper = new();
            for (int i = 0; i < numTasks; ++i)
            {
                asyncThreadHelper.Add(() => new ValueTask(FuncAsync()));
            }
        }

        [GlobalCleanup(Target = nameof(AsyncThreadHelper))]
        public void Cleanup()
        {
            asyncThreadHelper.Dispose();
        }

        [Benchmark]
        public ValueTask AsyncThreadHelper()
        {
            return asyncThreadHelper.ExecuteAndWaitAsync();
        }
    }
}