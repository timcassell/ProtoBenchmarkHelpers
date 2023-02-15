using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Proto.Utilities.Benchmark
{
    /// <summary>
    /// A helper class used to efficiently benchmark actions ran on multiple threads concurrently.
    /// </summary>
    public class BenchmarkThreadHelper : IDisposable, IEnumerable // IEnumerable only included because the C# compiler requires it to use collection initializer.
    {
        private static readonly int ProcessorCount = Environment.ProcessorCount;

        private class Node
        {
            internal Node next;
            internal Action action;
        }

        private readonly Barrier barrier = new(1);
        private readonly int maxConcurrency;

        private int runningThreadCount = 0;
        volatile private int pendingThreadCount;
        volatile private bool isDisposed;

        // Calling thread always consumes head, other threads store their initial consume node in their stack. Calling thread sets current to next before waking all threads.
        // When threads are done invoking, they will try to take current to continue invoking.
        private Node callerNode;
        private readonly Node headSentinel;
        private Node tail;
        private Node next;
        volatile private Node current;

        private readonly Thread[] threads;
        private List<Exception> exceptions;

        /// <summary>
        /// Create a new <see cref="BenchmarkThreadHelper"/> instance with an optional <paramref name="maxConcurrency"/>.
        /// </summary>
        /// <param name="maxConcurrency">Sets the maximum number of threads that can run actions in parallel. The default of -1 will use <see cref="Environment.ProcessorCount"/>.</param>
        public BenchmarkThreadHelper(int maxConcurrency = -1)
        {
            if (maxConcurrency == -1)
            {
                maxConcurrency = ProcessorCount;
            }
            else if (maxConcurrency == 0 || maxConcurrency < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be positive or -1. Value: " + maxConcurrency);
            }
            this.maxConcurrency = Math.Min(maxConcurrency, ProcessorCount); // We don't need to create more software threads than there are hardware threads available.
            threads = new Thread[this.maxConcurrency];
            // headSentinel's action is null, it will never be invoked.
            // We use headSentinel to produce a circular linked-list so we never need to check for null.
            headSentinel = new Node();
            headSentinel.next = headSentinel;
            tail = headSentinel;
            next = headSentinel;
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~BenchmarkThreadHelper()
        {
            if (!isDisposed)
            {
                DisposeCore();
            }
        }

        /// <summary>
        /// Dispose this instance to release background resources.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(BenchmarkThreadHelper));
            }

            GC.SuppressFinalize(this);
            DisposeCore();
        }

        private void DisposeCore()
        {
            isDisposed = true;
            // Remove the caller node so it will throw if the user attempts to invoke again.
            callerNode = null;
            // Overwrite the action on every node to do nothing, then signal the other threads to continue and exit gracefully.
            for (var node = headSentinel.next; node != headSentinel; node = node.next)
            {
                node.action = () => { };
            }
            barrier.SignalAndWait();
            // Join the threads to make sure they are completely cleaned up before this method returns to prevent background noise in benchmarks.
            for (int i = 0; i < runningThreadCount; ++i)
            {
                threads[i].Join();
            }
        }

        /// <summary>
        /// Add an action to be ran in parallel. This method is not thread-safe.
        /// </summary>
        /// <param name="action">The action to be ran in parallel.</param>
        public void Add(Action action)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(BenchmarkThreadHelper));
            }

            // We use a weak reference to prevent the thread from keeping this alive if it is never disposed.
            WeakReference<BenchmarkThreadHelper> weakReference = null;
            // Separate reference to the barrier so that the ThreadRunner does not capture `this`.
            var localBarrier = barrier;
            var node = new Node() { action = action, next = headSentinel };
            tail.next = node;
            tail = node;
            if (callerNode == null)
            {
                callerNode = node;
                // The first action will always be ran on the calling thread, we don't need to spawn a new thread.
                return;
            }

            if (runningThreadCount < maxConcurrency - 1) // Subtract 1 because we already have the caller thread to work with.
            {
                var threadIndex = runningThreadCount;
                ++runningThreadCount;
                localBarrier.AddParticipant();
                weakReference = new(this, false);
                var thread = new Thread(ThreadRunner) { IsBackground = true };
                threads[threadIndex] = thread;
                thread.Start();
            }
            else if (next == headSentinel)
            {
                next = node;
            }

            void ThreadRunner(object _)
            {
                do
                {
                    localBarrier.SignalAndWait();
                } while (TryInvoke(weakReference, node));

                localBarrier.RemoveParticipant();
            }
        }

        // NoInlining to ensure the strong reference never stays on the stack indefinitely, so the GC can collect.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryInvoke(WeakReference<BenchmarkThreadHelper> weakReference, Node node)
        {
            if (!weakReference.TryGetTarget(out var owner) || owner.isDisposed)
            {
                return false;
            }
            owner.InvokeThenNotifyThreadComplete(node.action);
            return true;
        }

        /// <summary>
        /// Run all actions in parallel and wait for them all to complete. This method is not thread-safe.
        /// </summary>
        /// <remarks>
        /// If any action throws an exception, this will throw an <see cref="AggregateException"/> containing all thrown exceptions.
        /// </remarks>
        /// <exception cref="AggregateException"/>
        public void ExecuteAndWait()
        {
            // Not checking disposed here since this is performance critical.
            // If this has been disposed, or if Add was not called before this, callernode will be null, so this will throw automatically.

            // Caller thread always gets the first action. No need to synchronize access until the barrier is signaled.
            var action = callerNode.action;
            current = next;
            var oldNext = headSentinel.next;
            headSentinel.next = headSentinel;
            pendingThreadCount = runningThreadCount + 1;

            barrier.SignalAndWait();

            InvokeThenNotifyThreadComplete(action);

            if (pendingThreadCount != 0)
            {
                WaitForThreads();
            }
            headSentinel.next = oldNext;

            var exs = exceptions;
            if (exs != null)
            {
                exceptions = null;
                throw new AggregateException(exs);
            }

            void WaitForThreads()
            {
                var spinner = new SpinWait();
                do
                {
                    if (spinner.NextSpinWillYield)
                    {
                        // Just do a simple thread yield and reset the spinner so it won't do a full sleep.
                        Thread.Yield();
                        spinner = new SpinWait();
                        continue;
                    }
                    spinner.SpinOnce();
                } while (pendingThreadCount != 0);
            }
        }

        private Node TakeNext()
        {
            // Current should not be null and Node.next is never null (creates a circular chain), so we don't need to check for null.
            Node node;
            do
            {
                node = current;
            } while (Interlocked.CompareExchange(ref current, node.next, node) != node);
            return node;
        }

        private void InvokeThenNotifyThreadComplete(Action action)
        {
            do
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    lock (barrier)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(e);
                    }
                }
                action = TakeNext().action;
            } while (action != null); // headSentinel's action is null, that's the tail of the list.
            
            Interlocked.Decrement(ref pendingThreadCount);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
