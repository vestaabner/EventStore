using System;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint
{
    [TestFixture]
    public class when_emitting_events_with_null_streamId : TestFixtureWithExistingEvents
    {
        private ProjectionCheckpoint _checkpoint;
        private Exception _lastException;
        private TestCheckpointManagerMessageHandler _readyHandler;

        [SetUp]
        public void setup()
        {
            _readyHandler = new TestCheckpointManagerMessageHandler();
            _checkpoint = new ProjectionCheckpoint(
                _ioDispatcher, new ProjectionVersion(1, 0, 0), null, _readyHandler,
                CheckpointTag.FromPosition(0, 100, 50), new TransactionFilePositionTagger(0), 250);
            try
            {
                _checkpoint.ValidateOrderAndEmitEvents(
                    new[]
                    {
                        new EmittedEventEnvelope(
                            new EmittedDataEvent(
                                null, Guid.NewGuid(), "type", true, "data", null, CheckpointTag.FromPosition(0, 40, 30), null))
                    });
            }
            catch (Exception ex)
            {
                _lastException = ex;
            }
        }

        [Test, ExpectedException(typeof(ArgumentNullException))]
        public void throws_invalid_operation_exception()
        {
            if (_lastException != null) throw _lastException;
        }
    }
}
