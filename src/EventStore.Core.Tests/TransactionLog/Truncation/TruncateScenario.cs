using System;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Tests.TransactionLog.Truncation
{
    public abstract class TruncateScenario : ReadIndexTestScenario
    {
        protected TFChunkDbTruncator Truncator;
        protected long TruncateCheckpoint = long.MinValue;

        protected TruncateScenario(int maxEntriesInMemTable = 100, int metastreamMaxCount = 1)
            : base(maxEntriesInMemTable, metastreamMaxCount)
        {
        }

        public override void TestFixtureSetUp()
        {
            base.TestFixtureSetUp();

            if (TruncateCheckpoint == long.MinValue)
                throw new InvalidOperationException("AckCheckpoint must be set in WriteTestScenario.");

            OnBeforeTruncating();

            // need to close db before truncator can delete files

            ReadIndex.Close();
            ReadIndex.Dispose();

            TableIndex.Close(removeFiles: false);

            Db.Close();
            Db.Dispose();

            var truncator = new TFChunkDbTruncator(Db.Config);
            truncator.TruncateDb(TruncateCheckpoint);
        }

        protected virtual void OnBeforeTruncating()
        {
        }
    }
}