using System;
using System.Collections.Generic;
using System.Threading;

namespace Proto.Utilities.Benchmark
{
    /// <summary>
    /// A helper class used to efficiently benchmark actions ran on multiple threads concurrently.
    /// </summary>
    public class BenchmarkThreadHelper : IDisposable
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

        private List<Exception> exceptions;

        /// <summary>
        /// Create a new <see cref="BenchmarkThreadHelper"/> instance with an optional <see cref="maxConcurrency"/>.
        /// </summary>
        /// <param name="maxConcurrency">Sets the maximum number of threads that can run actions in parallel. -1 means no maximum. Default -1.</param>
        public BenchmarkThreadHelper(int maxConcurrency = -1)
        {
            if (maxConcurrency == 0 || maxConcurrency < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be positive or -1. Value: " + maxConcurrency);
            }
            this.maxConcurrency = maxConcurrency;
            headSentinel = new Node { action = () => { } };
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
                throw new InvalidOperationException("Object is disposed");
            }

            GC.SuppressFinalize(this);
            DisposeCore();
        }

        private void DisposeCore()
        {
            isDisposed = true;
            // Overwrite the action on every node to do nothing, then signal the other threads to continue and exit gracefully.
            for (var node = headSentinel.next; node != headSentinel; node = node.next)
            {
                node.action = () => { };
            }
            barrier.SignalAndWait();
            // Remove the action on every node so it will throw if the user attempts to invoke again.
            for (var node = headSentinel.next; node != headSentinel; node = node.next)
            {
                node.action = null;
            }
        }

        /// <summary>
        /// Add an action to be ran in parallel. This method is not thread-safe.
        /// </summary>
        /// <param name="action">The action to be ran in parallel.</param>
        public void AddAction(Action action)
        {
            if (isDisposed)
            {
                throw new InvalidOperationException("Object is disposed");
            }

            var node = new Node() { action = action, next = headSentinel };
            tail.next = node;
            tail = node;
            if (callerNode == null)
            {
                callerNode = node;
                // The first action will always be ran on the calling thread, we don't need to spawn a new thread.
                return;
            }

            // We don't need to store a reference to the thread, it will stay alive until this is disposed or the process terminates.
            // If maxConcurrency is unconstrained, we can more efficiently execute a single action on each thread.
            if (maxConcurrency == -1)
            {
                ++runningThreadCount;
                barrier.AddParticipant();
                new Thread(ThreadRunnerIndividual) { IsBackground = true }.Start(node);
            }
            else if (runningThreadCount < maxConcurrency - 1)
            {
                ++runningThreadCount;
                barrier.AddParticipant();
                new Thread(ThreadRunnerMultiple) { IsBackground = true }.Start(node);
            }
            else if (next == headSentinel)
            {
                next = node;
            }
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
            // If this has been disposed, or if AddAction was not called before this, head will be null or its action will be null, so this will throw automatically.

            pendingThreadCount = runningThreadCount + 1;
            // Caller thread always gets the first action. No need to synchronize access until the barrier is signaled.
            var node = callerNode;
            current = next;

            var oldNext = headSentinel.next;
            headSentinel.next = headSentinel;

            barrier.SignalAndWait();

            Invoke(node);
            InvokeRemainingActionsThenNotifyThreadComplete();

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
                // If there are more threads running than there are hardware threads to run them on, yield this thread via Monitor.Wait.
                if (pendingThreadCount > ProcessorCount)
                {
                    MonitorWait();
                    return;
                }

                var spinner = new SpinWait();
                do
                {
                    if (spinner.NextSpinWillYield)
                    {
                        // Just do a simple thread yield and reset the spinner so it won't do a full sleep.
                        spinner = new SpinWait();
                        Thread.Yield();
                        continue;
                    }
                    spinner.SpinOnce();
                } while (pendingThreadCount != 0);
            }

            void MonitorWait()
            {
                lock (barrier)
                {
                    if (pendingThreadCount != 0)
                    {
                        Monitor.Wait(barrier);
                    }
                }
            }
        }

        private void ThreadRunnerMultiple(object state)
        {
            Node node = (Node) state;
            while (!isDisposed)
            {
                barrier.SignalAndWait();
                Invoke(node);
                InvokeRemainingActionsThenNotifyThreadComplete();
            }
            barrier.RemoveParticipant();
        }

        private void ThreadRunnerIndividual(object state)
        {
            Node node = (Node) state;
            while (!isDisposed)
            {
                barrier.SignalAndWait();
                Invoke(node);
                NotifyThreadComplete();
            }
            barrier.RemoveParticipant();
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

        private void InvokeRemainingActionsThenNotifyThreadComplete()
        {
            var node = TakeNext();
            while (node != headSentinel)
            {
                Invoke(node);
                node = TakeNext();
            }
            NotifyThreadComplete();
        }

        private void Invoke(Node node)
        {
            try
            {
                node.action();
            }
            catch (Exception e)
            {
                lock (barrier)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(e);
                }
            }
        }

        private void NotifyThreadComplete()
        {
            if (Interlocked.Decrement(ref pendingThreadCount) == 0)
            {
                lock (barrier)
                {
                    Monitor.Pulse(barrier);
                }
            }
        }
    }
}
