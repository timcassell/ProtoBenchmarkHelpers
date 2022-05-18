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
        private class Node
        {
            internal Node next;
            internal readonly Action action;

            internal Node(Action action)
            {
                this.action = action;
            }
        }

        private readonly Barrier barrier = new(1);
        private readonly int maxConcurrency;

        private int runningThreadCount = 0;
        volatile private int pendingThreadCount;
        volatile private bool isDisposed;
        private Node head, tail;
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
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~BenchmarkThreadHelper()
        {
            isDisposed = true;
        }

        /// <summary>
        /// Dispose this instance to release background resources.
        /// </summary>
        public void Dispose()
        {
            isDisposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Add an action to be ran in parallel. This method is not thread-safe.
        /// </summary>
        /// <param name="action">The action to be ran in parallel.</param>
        public void AddAction(Action action)
        {
            var node = new Node(action);
            if (head == null)
            {
                head = node;
                tail = node;
            }
            else
            {
                tail.next = node;
                tail = node;
            }
            if (maxConcurrency == -1 || runningThreadCount < maxConcurrency - 1)
            {
                ++runningThreadCount;
                barrier.AddParticipant();
                // We don't need to store a reference to the thread, it will stay alive until this is disposed or the process terminates.
                // If maxConcurrency is unconstrained, we can more efficiently execute a single action on each thread.
                if (maxConcurrency == -1)
                {
                    new Thread(ThreadRunnerIndividual) { IsBackground = true }.Start(action);
                }
                else
                {
                    new Thread(ThreadRunnerMultiple) { IsBackground = true }.Start();
                }
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
            pendingThreadCount = runningThreadCount + 1;
            // Caller thread always gets the first action. No need to synchronize access until the barrier is signaled.
            var node = head;
            current = node.next;
            
            barrier.SignalAndWait();

            InvokeAction(node.action);
            MaybeInvokeActions(TakeNext());

            if (pendingThreadCount != 0)
            {
                WaitForThreads();
            }

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
                        MonitorWait();
                        return;
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

        private void ThreadRunnerMultiple(object _)
        {
            while (!isDisposed)
            {
                barrier.SignalAndWait();
                MaybeInvokeActions(TakeNext());
            }
        }

        private void ThreadRunnerIndividual(object state)
        {
            Action action = (Action) state;
            while (!isDisposed)
            {
                barrier.SignalAndWait();
                InvokeAction(action);
                NotifyThreadComplete();
            }
        }

        private Node TakeNext()
        {
            Node node;
            do
            {
                node = current;
                if (node == null)
                {
                    break;
                }
            } while (Interlocked.CompareExchange(ref current, node.next, node) == node);
            return node;
        }

        private void MaybeInvokeActions(Node node)
        {
            while (node != null)
            {
                InvokeAction(node.action);
                node = TakeNext();
            }
            NotifyThreadComplete();
        }

        private void InvokeAction(Action action)
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
