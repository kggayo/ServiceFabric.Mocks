﻿using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceFabric.Mocks.ReliableCollections
{
    /// <summary>
    /// Implements IReliableQueue.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MockReliableQueue<T> : TransactedCollection, IReliableQueue<T>
    {
        private readonly SerializedQueue<T> _queue;
        private readonly Lock<long> _lock = new Lock<long>();

        protected long QueueCount => _queue.Count;

        public MockReliableQueue(Uri uri, SerializerCollection serializers = null)
            : base(uri)
        {
            _queue = new SerializedQueue<T>(serializers);
        }

        public override void ReleaseLocks(ITransaction tx)
        {
            _lock.Release(tx.TransactionId);
        }

        public Task ClearAsync()
        {
            _queue.Clear();

            return Task.FromResult(true);
        }

        public async Task<IAsyncEnumerable<T>> CreateEnumerableAsync(ITransaction tx)
        {
            await _lock.Acquire(BeginTransaction(tx).TransactionId, LockMode.Default, default(TimeSpan), CancellationToken.None);
            return new MockAsyncEnumerable<T>(_queue);
        }

        public Task EnqueueAsync(ITransaction tx, T item)
        {
            return EnqueueAsync(tx, item, default, CancellationToken.None);
        }

        public async Task EnqueueAsync(ITransaction tx, T item, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await _lock.Acquire(BeginTransaction(tx).TransactionId, LockMode.Update, timeout, cancellationToken);
            _queue.Enqueue(item);
            AddAbortAction(tx, () => { _queue.Remove(item); return true; });
        }

        public async Task<long> GetCountAsync(ITransaction tx)
        {
            await _lock.Acquire(BeginTransaction(tx).TransactionId, LockMode.Default, default(TimeSpan), CancellationToken.None);

            return _queue.Count;
        }

        public Task<ConditionalValue<T>> TryDequeueAsync(ITransaction tx)
        {
            return TryDequeueAsync(tx, default(TimeSpan), CancellationToken.None);
        }

        public async Task<ConditionalValue<T>> TryDequeueAsync(ITransaction tx, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await _lock.Acquire(BeginTransaction(tx).TransactionId, LockMode.Update, timeout, cancellationToken);
            if (_queue.Count > 0)
            {
                T item = _queue.Dequeue();
                AddAbortAction(tx, () => 
                { 
                    _queue.Push(item); 
                    return true; 
                });

                return new ConditionalValue<T>(true, item);
            }

            return new ConditionalValue<T>();
        }

        public Task<ConditionalValue<T>> TryPeekAsync(ITransaction tx)
        {
            return TryPeekAsync(tx, LockMode.Default);
        }

        public Task<ConditionalValue<T>> TryPeekAsync(ITransaction tx, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return TryPeekAsync(tx, LockMode.Default, timeout, cancellationToken);
        }

        public Task<ConditionalValue<T>> TryPeekAsync(ITransaction tx, LockMode lockMode)
        {
            return TryPeekAsync(tx, lockMode, default, default);
        }

        public async Task<ConditionalValue<T>> TryPeekAsync(ITransaction tx, LockMode lockMode, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await _lock.Acquire(BeginTransaction(tx).TransactionId, lockMode, timeout, cancellationToken);
            if (_queue.Count > 0)
            {
                return new ConditionalValue<T>(true, _queue.Peek());
            }

            return new ConditionalValue<T>();
        }
    }
}
