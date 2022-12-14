using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.Index32Bit
{
    [TestFixture]
    public class when_trying_to_get_oldest_entry: SpecificationWithFile
    {
        protected byte _ptableVersion = PTableVersions.Index32Bit;
        private ulong GetHash(ulong value){
            return _ptableVersion == PTableVersions.Index32Bit ? value >> 32 : value;
        }
        [Test]
        public void nothing_is_found_on_empty_stream()
        {
            var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memTable.Add(0x010100000000, 0x01, 0xffff);
            using (var ptable = PTable.FromMemtable(memTable, Filename))
            {
                IndexEntry entry;
                Assert.IsFalse(ptable.TryGetOldestEntry(0x12, out entry));
            }
        }

        [Test]
        public void single_item_is_latest()
        {
            var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memTable.Add(0x010100000000, 0x01, 0xffff);
            using (var ptable = PTable.FromMemtable(memTable, Filename))
            {
                IndexEntry entry;
                Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
                Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
                Assert.AreEqual(0x01, entry.Version);
                Assert.AreEqual(0xffff, entry.Position);
            }
        }

        [Test]
        public void correct_entry_is_returned()
        {
            var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memTable.Add(0x010100000000, 0x01, 0xffff);
            memTable.Add(0x010100000000, 0x02, 0xfff2);
            using (var ptable = PTable.FromMemtable(memTable, Filename))
            {
                IndexEntry entry;
                Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
                Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
                Assert.AreEqual(0x01, entry.Version);
                Assert.AreEqual(0xffff, entry.Position);
            }
        }

        [Test]
        public void when_duplicated_entries_exist_the_one_with_oldest_position_is_returned()
        {
            var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memTable.Add(0x010100000000, 0x01, 0xfff1);
            memTable.Add(0x010100000000, 0x02, 0xfff2);
            memTable.Add(0x010100000000, 0x01, 0xfff3);
            memTable.Add(0x010100000000, 0x02, 0xfff4);
            using (var ptable = PTable.FromMemtable(memTable, Filename))
            {
                IndexEntry entry;
                Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
                Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
                Assert.AreEqual(0x01, entry.Version);
                Assert.AreEqual(0xfff1, entry.Position);
            }
        }

        [Test]
        public void only_entry_with_smallest_position_is_returned_when_triduplicated()
        {
            var memTable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memTable.Add(0x010100000000, 0x01, 0xfff1);
            memTable.Add(0x010100000000, 0x01, 0xfff3);
            memTable.Add(0x010100000000, 0x01, 0xfff5);
            using (var ptable = PTable.FromMemtable(memTable, Filename))
            {
                IndexEntry entry;
                Assert.IsTrue(ptable.TryGetOldestEntry(0x010100000000, out entry));
                Assert.AreEqual(GetHash(0x010100000000), entry.Stream);
                Assert.AreEqual(0x01, entry.Version);
                Assert.AreEqual(0xfff1, entry.Position);
            }
        }
    }
}
