using BenchmarkDotNet.Running;

namespace Proto.Utilities.Benchmark.Examples
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
