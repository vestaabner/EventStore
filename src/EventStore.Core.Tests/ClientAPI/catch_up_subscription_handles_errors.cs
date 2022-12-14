using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.ClientOperations;
using EventStore.ClientAPI.Common.Log;
using EventStore.ClientAPI.Internal;
using EventStore.ClientAPI.SystemData;
using NUnit.Framework;
using ClientMessage = EventStore.ClientAPI.Messages.ClientMessage;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;
using StreamMetadata = EventStore.ClientAPI.StreamMetadata;
using SystemSettings = EventStore.ClientAPI.SystemSettings;

namespace EventStore.Core.Tests.ClientAPI
{
    [TestFixture]
    public class catch_up_subscription_handles_errors
    {
        private static int TimeoutMs = 2000;
        private FakeEventStoreConnection _connection;
        private IList<ResolvedEvent> _raisedEvents;
        private bool _liveProcessingStarted;
        private bool _isDropped;
        private ManualResetEventSlim _dropEvent;
        private ManualResetEventSlim _raisedEventEvent;
        private Exception _dropException;
        private SubscriptionDropReason _dropReason;
        private EventStoreStreamCatchUpSubscription _subscription;
        private static readonly string StreamId = "stream1";

        [SetUp]
        public void SetUp()
        {
            _connection = new FakeEventStoreConnection();
            _raisedEvents = new List<ResolvedEvent>();
            _dropEvent = new ManualResetEventSlim();
            _raisedEventEvent = new ManualResetEventSlim();
            _liveProcessingStarted = false;
            _isDropped = false;
            _dropReason = SubscriptionDropReason.Unknown;
            _dropException = null;

            var settings = new CatchUpSubscriptionSettings(1, 1, false, false);
            _subscription = new EventStoreStreamCatchUpSubscription(_connection, new NoopLogger(), StreamId, null, null,
                (subscription, ev) =>
                {
                    _raisedEvents.Add(ev);
                    _raisedEventEvent.Set();
                },
                subscription =>
                {
                    _liveProcessingStarted = true;
                },
                (subscription, reason, ex) =>
                {
                    _isDropped = true;
                    _dropReason = reason;
                    _dropException = ex;
                    _dropEvent.Set();
                },
                settings);
        }

        [Test]
        public void read_events_til_stops_subscription_when_throws_immediately()
        {

            var expectedException = new ApplicationException("Test");

            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
                                                           {
                                                               Assert.That(stream, Is.EqualTo(StreamId));
                                                               Assert.That(start, Is.EqualTo(0));
                                                               Assert.That(max, Is.EqualTo(1));
                                                               throw expectedException;
                                                           });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
        }

        private void AssertStartFailsAndDropsSubscriptionWithException(ApplicationException expectedException)
        {
            Assert.That(() => _subscription.Start().Wait(TimeoutMs), Throws.TypeOf<AggregateException>());
            Assert.That(_isDropped);
            Assert.That(_dropReason, Is.EqualTo(SubscriptionDropReason.CatchUpError));
            Assert.That(_dropException, Is.SameAs(expectedException));
            Assert.That(_liveProcessingStarted, Is.False);
        }

        [Test]
        public void read_events_til_stops_subscription_when_throws_asynchronously()
        {
         
            var expectedException = new ApplicationException("Test");
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                Assert.That(stream, Is.EqualTo(StreamId));
                Assert.That(start, Is.EqualTo(0));
                Assert.That(max, Is.EqualTo(1));
                taskCompletionSource.SetException(expectedException);
                return taskCompletionSource.Task;
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
        }

        [Test]
        public void read_events_til_stops_subscription_when_second_read_throws_immediately()
        {
            var expectedException = new ApplicationException("Test");

            int callCount = 0;

            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                Assert.That(stream, Is.EqualTo(StreamId));
                Assert.That(max, Is.EqualTo(1));

                if (callCount++ == 0)
                {
                    Assert.That(start, Is.EqualTo(0));
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetResult(CreateStreamEventsSlice());
                    return taskCompletionSource.Task;

                }
                else
                {
                    Assert.That(start, Is.EqualTo(1));
                    throw expectedException;    
                }
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void read_events_til_stops_subscription_when_second_read_throws_asynchronously()
        {
            var expectedException = new ApplicationException("Test");

            int callCount = 0;

            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                Assert.That(stream, Is.EqualTo(StreamId));
                Assert.That(max, Is.EqualTo(1));

                if (callCount++ == 0)
                {
                    Assert.That(start, Is.EqualTo(0));
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetResult(CreateStreamEventsSlice());
                    return taskCompletionSource.Task;

                }
                else
                {
                    Assert.That(start, Is.EqualTo(1));
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetException(expectedException);
                    return taskCompletionSource.Task;
                }
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }       

        [Test]
        public void start_stops_subscription_if_subscribe_fails_immediately()
        {
            var expectedException = new ApplicationException("Test");

            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
                return taskCompletionSource.Task;
            });

            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                Assert.That(stream, Is.EqualTo(StreamId));
                throw expectedException;
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void start_stops_subscription_if_subscribe_fails_async()
        {
            var expectedException = new ApplicationException("Test");

            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
                return taskCompletionSource.Task;
            });

            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                taskCompletionSource.SetException(expectedException);
                return taskCompletionSource.Task;
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void start_stops_subscription_if_historical_missed_events_load_fails_immediate()
        {
            var expectedException = new ApplicationException("Test");

            int callCount = 0;
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                if (callCount++ == 0)
                {
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
                    return taskCompletionSource.Task;
                }
                else
                {
                    Assert.That(start, Is.EqualTo(1));
                    throw expectedException;
                }
            });

            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                taskCompletionSource.SetResult(CreateVolatileSubscription(raise, drop, 1));
                return taskCompletionSource.Task;
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }


        [Test]
        public void start_stops_subscription_if_historical_missed_events_load_fails_async()
        {
            var expectedException = new ApplicationException("Test");

            int callCount = 0;
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                if (callCount++ == 0)
                {
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
                    return taskCompletionSource.Task;
                }
                else
                {
                    Assert.That(start, Is.EqualTo(1));
                    var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                    taskCompletionSource.SetException(expectedException);
                    return taskCompletionSource.Task;
                }
            });

            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                taskCompletionSource.SetResult(CreateVolatileSubscription(raise, drop, 1));
                return taskCompletionSource.Task;
            });

            AssertStartFailsAndDropsSubscriptionWithException(expectedException);
            Assert.That(_raisedEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void start_completes_onces_subscription_is_live()
        {
            var finalEvent = new ManualResetEventSlim();
            int callCount = 0;
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                callCount++;

                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                if (callCount == 1)
                {
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(isEnd: true));
                }
                else if (callCount == 2)
                {
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(fromEvent:1, isEnd: true));
                    Assert.That(finalEvent.Wait(TimeoutMs));
                }
                
                return taskCompletionSource.Task;
            });

            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                taskCompletionSource.SetResult(CreateVolatileSubscription(raise, drop, 1));
                return taskCompletionSource.Task;
            });

            var task = _subscription.Start();

            Assert.That(task.Status, Is.Not.EqualTo(TaskStatus.RanToCompletion));
            
            finalEvent.Set();

            Assert.That(task.Wait(TimeoutMs));
        }

        [Test]
        public void when_live_processing_and_disconnected_reconnect_keeps_events_ordered()
        {
            int callCount = 0;
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                callCount++;

                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                if (callCount == 1)
                {
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(fromEvent: 0, count: 0, isEnd: true));
                }
                else if (callCount == 2)
                {
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(fromEvent: 0, count: 0, isEnd: true));
                }

                return taskCompletionSource.Task;
            });

            VolatileEventStoreSubscription volatileEventStoreSubscription = null;
            Action<EventStoreSubscription, SubscriptionDropReason, Exception> innerSubscriptionDrop = null;
            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                innerSubscriptionDrop = drop;
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                volatileEventStoreSubscription = CreateVolatileSubscription(raise, drop, null);
                taskCompletionSource.SetResult(volatileEventStoreSubscription);
                return taskCompletionSource.Task;
            });

            Assert.That(_subscription.Start().Wait(TimeoutMs));
            Assert.That(_raisedEvents.Count, Is.EqualTo(0));

            Assert.That(innerSubscriptionDrop, Is.Not.Null);
            innerSubscriptionDrop(volatileEventStoreSubscription, SubscriptionDropReason.ConnectionClosed, null);

            Assert.That(_dropEvent.Wait(TimeoutMs));
            _dropEvent.Reset();

            var waitForOutOfOrderEvent = new ManualResetEventSlim();
            callCount = 0;
            _connection.HandleReadStreamEventsForwardAsync((stream, start, max) =>
            {
                callCount++;

                var taskCompletionSource = new TaskCompletionSource<StreamEventsSlice>();
                if (callCount == 1)
                {
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(fromEvent: 0, count:0, isEnd: true));
                }
                else if (callCount == 2)
                {
                    Assert.That(waitForOutOfOrderEvent.Wait(TimeoutMs));
                    taskCompletionSource.SetResult(CreateStreamEventsSlice(fromEvent: 0, count: 1, isEnd: true));
                }

                return taskCompletionSource.Task;
            });

            var event1 = new ClientMessage.ResolvedEvent(new ClientMessage.EventRecord(StreamId, 1, Guid.NewGuid().ToByteArray(), null, 0, 0, null, null, null, null), null, 0, 0);
            
            _connection.HandleSubscribeToStreamAsync((stream, raise, drop) =>
            {
                var taskCompletionSource = new TaskCompletionSource<EventStoreSubscription>();
                VolatileEventStoreSubscription volatileEventStoreSubscription2 = CreateVolatileSubscription(raise, drop, null);
                taskCompletionSource.SetResult(volatileEventStoreSubscription);

                raise(volatileEventStoreSubscription2, new ResolvedEvent(event1));
                
                return taskCompletionSource.Task;
            });


            var reconnectTask = Task.Factory.StartNew(() =>
            {
                _connection.OnConnected(new ClientConnectionEventArgs(_connection, new IPEndPoint(IPAddress.Any, 1)));

            }, TaskCreationOptions.AttachedToParent);

            Assert.That(_raisedEventEvent.Wait(100), Is.False);

            waitForOutOfOrderEvent.Set();

            Assert.That(_raisedEventEvent.Wait(TimeoutMs));

            Assert.That(_raisedEvents[0].OriginalEventNumber, Is.EqualTo(0));
            Assert.That(_raisedEvents[1].OriginalEventNumber, Is.EqualTo(1));

            Assert.That(reconnectTask.Wait(TimeoutMs));
        }

        private static VolatileEventStoreSubscription CreateVolatileSubscription(Action<EventStoreSubscription, ResolvedEvent> raise, Action<EventStoreSubscription, SubscriptionDropReason, Exception> drop, int? lastEventNumber)
        {
            return new VolatileEventStoreSubscription(new VolatileSubscriptionOperation(new NoopLogger(), new TaskCompletionSource<EventStoreSubscription>(), StreamId, false, null, raise, drop, false, () => null), StreamId, -1, lastEventNumber);
        }

        private static StreamEventsSlice CreateStreamEventsSlice(int fromEvent = 0, int count = 1, bool isEnd = false)
        {
            var events = Enumerable.Range(0, count)
                .Select(
                    i =>
                        new ClientMessage.ResolvedIndexedEvent(
                            new ClientMessage.EventRecord(StreamId, i, Guid.NewGuid().ToByteArray(), null, 0, 0, null,
                                null, null, null), null))
                .ToArray();

            return new StreamEventsSlice(SliceReadStatus.Success, StreamId, fromEvent, ReadDirection.Forward, events, fromEvent + count, 100, isEnd);
        }
    }

    internal class FakeEventStoreConnection : IEventStoreConnection
    {
        private Func<Position, int, bool, UserCredentials, Task<AllEventsSlice>> _readAllEventsForwardAsync;
        private Func<string, int, int, Task<StreamEventsSlice>> _readStreamEventsForwardAsync;
        private Func<string, Action<EventStoreSubscription, ResolvedEvent>, Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>> _subscribeToStreamAsync;

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public string ConnectionName { get; private set; }
        public ConnectionSettings Settings { get { return null; } }

        public Task ConnectAsync()
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }

        public Task<DeleteResult> DeleteStreamAsync(string stream, int expectedVersion, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<DeleteResult> DeleteStreamAsync(string stream, int expectedVersion, bool hardDelete, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<WriteResult> AppendToStreamAsync(string stream, int expectedVersion, params EventData[] events)
        {
            throw new NotImplementedException();
        }

        public Task<WriteResult> AppendToStreamAsync(string stream, int expectedVersion, UserCredentials userCredentials, params EventData[] events)
        {
            throw new NotImplementedException();
        }

        public Task<WriteResult> AppendToStreamAsync(string stream, int expectedVersion, IEnumerable<EventData> events, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<EventStoreTransaction> StartTransactionAsync(string stream, int expectedVersion, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public EventStoreTransaction ContinueTransaction(long transactionId, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<EventReadResult> ReadEventAsync(string stream, int eventNumber, bool resolveLinkTos, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public void HandleReadStreamEventsForwardAsync(Func<string, int, int, Task<StreamEventsSlice>> callback)
        {
            _readStreamEventsForwardAsync = callback;
        }

        public Task<StreamEventsSlice> ReadStreamEventsForwardAsync(string stream, int start, int count, bool resolveLinkTos,
            UserCredentials userCredentials = null)
        {
            return _readStreamEventsForwardAsync(stream, start, count);
        }

        public Task<StreamEventsSlice> ReadStreamEventsBackwardAsync(string stream, int start, int count, bool resolveLinkTos,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public void HandleReadAllEventsForwardAsync(Func<Position, int, bool, UserCredentials, Task<AllEventsSlice>> callback)
        {
            _readAllEventsForwardAsync = callback;
        }

        public Task<AllEventsSlice> ReadAllEventsForwardAsync(Position position, int maxCount, bool resolveLinkTos,
            UserCredentials userCredentials = null)
        {
            return _readAllEventsForwardAsync(position, maxCount, resolveLinkTos, userCredentials);
        }

        public Task<AllEventsSlice> ReadAllEventsBackwardAsync(Position position, int maxCount, bool resolveLinkTos,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public void HandleSubscribeToStreamAsync(Func<string, Action<EventStoreSubscription, ResolvedEvent>, Action<EventStoreSubscription, SubscriptionDropReason, Exception>, Task<EventStoreSubscription>> callback)
        {
            _subscribeToStreamAsync = callback;
        }

        public Task<EventStoreSubscription> SubscribeToStreamAsync(string stream, bool resolveLinkTos, Action<EventStoreSubscription, ResolvedEvent> eventAppeared, Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
            UserCredentials userCredentials = null)
        {
            return _subscribeToStreamAsync(stream, eventAppeared, subscriptionDropped);
        }

        public EventStoreStreamCatchUpSubscription SubscribeToStreamFrom(string stream, int? lastCheckpoint, bool resolveLinkTos,
            Action<EventStoreCatchUpSubscription, ResolvedEvent> eventAppeared, Action<EventStoreCatchUpSubscription> liveProcessingStarted = null, Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
            UserCredentials userCredentials = null, int readBatchSize = 500)
        {
            throw new NotImplementedException();
        }

        public EventStoreStreamCatchUpSubscription SubscribeToStreamFrom(
            string stream,
            int? lastCheckpoint,
            CatchUpSubscriptionSettings settings,
            Action<EventStoreCatchUpSubscription, ResolvedEvent> eventAppeared,
            Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
            Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<EventStoreSubscription> SubscribeToAllAsync(bool resolveLinkTos, Action<EventStoreSubscription, ResolvedEvent> eventAppeared, Action<EventStoreSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public EventStorePersistentSubscriptionBase ConnectToPersistentSubscription(string stream, string groupName,
            Action<EventStorePersistentSubscriptionBase, ResolvedEvent> eventAppeared, Action<EventStorePersistentSubscriptionBase, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null, int bufferSize = 10,
            bool autoAck = true)
        {
            throw new NotImplementedException();
        }

        public EventStoreAllCatchUpSubscription SubscribeToAllFrom(Position? lastCheckpoint, bool resolveLinkTos, Action<EventStoreCatchUpSubscription, ResolvedEvent> eventAppeared,
            Action<EventStoreCatchUpSubscription> liveProcessingStarted = null, Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null,
            int readBatchSize = 500)
        {
            throw new NotImplementedException();
        }

        public EventStoreAllCatchUpSubscription SubscribeToAllFrom(
            Position? lastCheckpoint,
            CatchUpSubscriptionSettings settings,
            Action<EventStoreCatchUpSubscription, ResolvedEvent> eventAppeared,
            Action<EventStoreCatchUpSubscription> liveProcessingStarted = null,
            Action<EventStoreCatchUpSubscription, SubscriptionDropReason, Exception> subscriptionDropped = null,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task UpdatePersistentSubscriptionAsync(string stream, string groupName, PersistentSubscriptionSettings settings,
            UserCredentials credentials)
        {
            throw new NotImplementedException();
        }

        public Task CreatePersistentSubscriptionAsync(string stream, string groupName, PersistentSubscriptionSettings settings,
            UserCredentials credentials)
        {
            throw new NotImplementedException();
        }

        public Task DeletePersistentSubscriptionAsync(string stream, string groupName, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<WriteResult> SetStreamMetadataAsync(string stream, int expectedMetastreamVersion, StreamMetadata metadata,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<WriteResult> SetStreamMetadataAsync(string stream, int expectedMetastreamVersion, byte[] metadata,
            UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<StreamMetadataResult> GetStreamMetadataAsync(string stream, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task<RawStreamMetadataResult> GetStreamMetadataAsRawBytesAsync(string stream, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public Task SetSystemSettingsAsync(SystemSettings settings, UserCredentials userCredentials = null)
        {
            throw new NotImplementedException();
        }

        public event EventHandler<ClientConnectionEventArgs> Connected;
        public event EventHandler<ClientConnectionEventArgs> Disconnected;
        public event EventHandler<ClientReconnectingEventArgs> Reconnecting;
        public event EventHandler<ClientClosedEventArgs> Closed;
        public event EventHandler<ClientErrorEventArgs> ErrorOccurred;
        public event EventHandler<ClientAuthenticationFailedEventArgs> AuthenticationFailed;

        protected virtual void OnErrorOccurred(ClientErrorEventArgs e)
        {
            var handler = ErrorOccurred;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnAuthenticationFailed(ClientAuthenticationFailedEventArgs e)
        {
            var handler = AuthenticationFailed;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnClosed(ClientClosedEventArgs e)
        {
            var handler = Closed;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnReconnecting(ClientReconnectingEventArgs e)
        {
            var handler = Reconnecting;
            if (handler != null) handler(this, e);
        }

        protected virtual void OnDisconnected(ClientConnectionEventArgs e)
        {
            var handler = Disconnected;
            if (handler != null) handler(this, e);
        }

        public void OnConnected(ClientConnectionEventArgs e)
        {
            var handler = Connected;
            if (handler != null) handler(this, e);
        }

        public Task<EventStorePersistentSubscriptionBase> ConnectToPersistentSubscriptionAsync(string stream, string groupName, Action<EventStorePersistentSubscriptionBase, ResolvedEvent> eventAppeared, Action<EventStorePersistentSubscriptionBase, SubscriptionDropReason, Exception> subscriptionDropped = null, UserCredentials userCredentials = null, int bufferSize = 10, bool autoAck = true)
        {
            throw new NotImplementedException();
        }
    }
}
