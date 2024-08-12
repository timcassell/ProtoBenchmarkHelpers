using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace Proto.Utilities.Benchmark
{
    /// <summary>
    /// A helper class used to efficiently benchmark async actions started on multiple threads concurrently.
    /// </summary>
    public class AsyncBenchmarkThreadHelper : IValueTaskSource, IDisposable, IEnumerable // IEnumerable only included because the C# compiler requires it to use collection initializer.
    {
        private static readonly int ProcessorCount = Environment.ProcessorCount;

        private class Node
        {
            internal Node next;
            internal Func<ValueTask> action;

            internal readonly AsyncBenchmarkThreadHelper owner;
            private readonly Action continuation;
            private ValueTaskAwaiter awaiter;

            internal Node(AsyncBenchmarkThreadHelper owner)
            {
                this.owner = owner;
                continuation = Continuation;
            }

            internal void AwaitCompletion(ValueTaskAwaiter awaiter)
            {
                this.awaiter = awaiter;
                awaiter.UnsafeOnCompleted(continuation);
            }

            private void Continuation()
            {
                try
                {
                    awaiter.GetResult();
                }
                catch (Exception e)
                {
                    owner.RecordException(e);
                }
                awaiter = default;
                owner.InvokeNextFromAwaiter();
            }
        }

        private readonly Barrier barrier = new(1);
        private readonly int maxConcurrency;

        private int runningThreadCount = 0;
        volatile private int pendingThreadCount;
        volatile private bool isDisposed;
        volatile private bool isExecuting;

        // Calling thread always consumes head, other threads store their initial consume node in their stack. Calling thread sets current to next before waking all threads.
        // When tasks are complete, they will try to take current to continue invoking.
        private Node callerNode;
        private readonly Node headSentinel;
        private Node initialCallerNode;
        private Node oldSentinelNext;
        private Node tail;
        private Node next;
        volatile private Node current;

        private readonly Thread[] threads;
        private List<Exception> exceptions;
        volatile private Action<object> continuation = noopContinuation;
        private object continuationState;
        private readonly ManualResetEventSlim completionEvent = new(false);

        private static readonly Action<object> blockingContinuation = obj => Unsafe.As<ManualResetEventSlim>(obj).Set();
        private static readonly Action<object> noopContinuation = _ => { };

        /// <summary>
        /// Create a new <see cref="AsyncBenchmarkThreadHelper"/> instance with an optional <paramref name="maxConcurrency"/>.
        /// </summary>
        /// <param name="maxConcurrency">Sets the maximum number of threads that can run actions in parallel. The default of -1 will use <see cref="Environment.ProcessorCount"/>.</param>
        public AsyncBenchmarkThreadHelper(int maxConcurrency = -1)
        {
            if (maxConcurrency == -1)
            {
                maxConcurrency = ProcessorCount;
            }
            else if (maxConcurrency == 0 || maxConcurrency < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be positive or -1. Value: " + maxConcurrency);
            }
            this.maxConcurrency = maxConcurrency;
            threads = new Thread[this.maxConcurrency];
            // headSentinel's action is null, it will never be invoked.
            // We use headSentinel to produce a circular linked-list so we never need to check for null.
            headSentinel = new Node(this);
            headSentinel.next = headSentinel;
            tail = headSentinel;
            next = headSentinel;
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~AsyncBenchmarkThreadHelper()
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
            if (isDisposed | isExecuting)
            {
                throw isDisposed
                    ? new ObjectDisposedException(nameof(AsyncBenchmarkThreadHelper))
                    : new InvalidOperationException("Cannot Dispose while the execution is still pending.");
            }

            GC.SuppressFinalize(this);
            DisposeCore();
            completionEvent.Dispose();
        }

        private void DisposeCore()
        {
            isDisposed = true;
            // Remove the caller node so it will throw if the user attempts to invoke again.
            callerNode = null;
            // Overwrite the action on every node to do nothing, then signal the other threads to continue and exit gracefully.
            for (var node = headSentinel.next; node != headSentinel; node = node.next)
            {
                node.action = () => new ValueTask();
            }
            barrier.SignalAndWait();
            // Join the threads to make sure they are completely cleaned up before this method returns to prevent background noise in benchmarks.
            for (int i = 0; i < runningThreadCount; ++i)
            {
                threads[i].Join();
            }
        }

        /// <summary>
        /// Add an async action to be ran in parallel. This method is not thread-safe.
        /// </summary>
        /// <param name="action">The async action to be ran in parallel.</param>
        public void Add(Func<ValueTask> action)
        {
            if (isDisposed | isExecuting)
            {
                throw isDisposed
                    ? new ObjectDisposedException(nameof(AsyncBenchmarkThreadHelper))
                    : new InvalidOperationException("Cannot Add an action while the execution is still pending.");
            }

            // Separate reference to the barrier so that the ThreadRunner does not capture `this`.
            var localBarrier = barrier;
            var node = new Node(this) { action = action, next = headSentinel };
            tail.next = node;
            tail = node;
            if (initialCallerNode == null)
            {
                callerNode = node;
                initialCallerNode = node;
                // The first action will always be ran on the calling thread, we don't need to spawn a new thread.
                return;
            }

            if (runningThreadCount < maxConcurrency - 1) // Subtract 1 because we already have the caller thread to work with.
            {
                var threadIndex = runningThreadCount;
                ++runningThreadCount;
                localBarrier.AddParticipant();
                // We use a weak reference to prevent the thread from keeping this alive if it is never disposed.
                var weakReference = new WeakReference<Node>(node, false);
                var thread = new Thread(ThreadRunner) { IsBackground = true };
                threads[threadIndex] = thread;
                thread.Start();

                void ThreadRunner(object _)
                {
                    do
                    {
                        localBarrier.SignalAndWait();
                    } while (TryInvoke(weakReference));

                    localBarrier.RemoveParticipant();
                }
            }
            else if (next == headSentinel)
            {
                next = node;
            }
        }

        // NoInlining to ensure the strong reference never stays on the stack indefinitely, so the GC can collect.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryInvoke(WeakReference<Node> weakReference)
        {
            if (!weakReference.TryGetTarget(out var node) || node.owner.isDisposed)
            {
                return false;
            }
            node.owner.InvokeThenNotifyComplete(node, node.action);
            return true;
        }

        /// <summary>
        /// Run all actions in parallel and wait for them all to complete asynchronously. This method is not thread-safe.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> that will be completed when all actions have been completed.</returns>
        /// <remarks>
        /// If any action throws an exception, or any <see cref="ValueTask"/> returned from an action is faulted or canceled,
        /// the returned <see cref="ValueTask"/> will be faulted with an <see cref="AggregateException"/> containing all exceptions.
        /// </remarks>
        public ValueTask ExecuteAndWaitAsync()
        {
            // Not checking disposed or executing state here since this is performance critical.
            // If this has been disposed, or if Add was not called before this, or if this is already executing,
            // callernode will be null, so this will throw automatically.

            // Caller thread always gets the first action. No need to synchronize access until the barrier is signaled.
            var node = callerNode;
            var action = callerNode.action;
            // We set the callerNode to null so if this is called again before the previous call is complete, this will throw.
            // It will be set back to the initialCallerNode when the execution is complete.
            callerNode = null;
            isExecuting = true;
            pendingThreadCount = runningThreadCount + 1;
            current = next;
            oldSentinelNext = headSentinel.next;
            headSentinel.next = headSentinel;

            barrier.SignalAndWait();

            InvokeThenNotifyComplete(node, action);

            // We ignore the token for efficiency.
            return new ValueTask(this, 0);
        }

        private void OnThreadComplete()
        {
            if (Interlocked.Decrement(ref pendingThreadCount) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref continuation, null).Invoke(continuationState);
        }

        private void RecordException(Exception e)
        {
            lock (barrier)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(e);
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

        private void InvokeThenNotifyComplete(Node node, Func<ValueTask> action)
        {
            do
            {
                try
                {
                    var awaiter = action().GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        node.AwaitCompletion(awaiter);
                        return; // We break the loop and skip the thread complete notification, because the Node's continuation will handle it.
                    }
                }
                catch (Exception e)
                {
                    RecordException(e);
                }
                node = TakeNext();
                action = node.action;
            } while (action != null); // headSentinel's action is null, that's the tail of the list.

            OnThreadComplete();
        }

        private void InvokeNextFromAwaiter()
        {
            Node node = TakeNext();
            var action = node.action;
            if (action == null)
            {
                OnThreadComplete();
                return;
            }
            InvokeThenNotifyComplete(node, action);
        }

        // Currently BenchmarkDotNet will never call GetStatus or OnCompleted, it only calls GetResult. But we set them up here for proper await usage regardless.
        // We don't validate the token since we want to be as efficient as possible.
        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
        {
            // Canceled state not supported. A canceled action will result in this being faulted.
            return continuation == noopContinuation ? ValueTaskSourceStatus.Pending
                : exceptions != null ? ValueTaskSourceStatus.Faulted
                : ValueTaskSourceStatus.Succeeded;
        }

        void IValueTaskSource.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            // We ignore the flags.
            continuationState = state;
            // We must write the state before we write the delegate.
            if (Interlocked.CompareExchange(ref this.continuation, continuation, noopContinuation) != noopContinuation)
            {
                // Already complete.
                continuation(state);
            }
        }

        void IValueTaskSource.GetResult(short token)
        {
            // BenchmarkDotNet illegally waits on ValueTasks by calling `ValueTask.GetAwaiter().GetResult()`. See https://github.com/dotnet/BenchmarkDotNet/issues/1595
            // We have to support that usage here to block the calling thread until this is complete.
            // NOTE: This was fixed in BenchmarkDotNet v0.14.0. If you are using 0.14.0 or newer, this check and WaitForComplete() can be removed.
            if (continuation == noopContinuation)
            {
                WaitForComplete();
            }

            var exs = exceptions;
            Reset();
            if (exs != null)
            {
                throw new AggregateException(exs);
            }

            void WaitForComplete()
            {
                var spinner = new SpinWait();
                do
                {
                    if (spinner.NextSpinWillYield)
                    {
                        // CompareExchange to make sure it wasn't invoked before we block.
                        continuationState = completionEvent;
                        // We must write the state before we write the delegate.
                        if (Interlocked.CompareExchange(ref continuation, blockingContinuation, noopContinuation) == noopContinuation)
                        {
                           completionEvent.Wait();
                        }
                        return;
                    }
                    spinner.SpinOnce();
                } while (continuation == noopContinuation);
            }
        }

        private void Reset()
        {
            headSentinel.next = oldSentinelNext;
            exceptions = null;
            continuation = noopContinuation;
            continuationState = null;
            callerNode = initialCallerNode;
            isExecuting = false;
            completionEvent.Reset();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}