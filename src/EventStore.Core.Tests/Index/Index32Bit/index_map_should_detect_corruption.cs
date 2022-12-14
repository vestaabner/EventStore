using System;
using System.IO;
using System.Linq;
using EventStore.Core.Exceptions;
using EventStore.Core.Index;
using NUnit.Framework;

namespace EventStore.Core.Tests.Index.Index32Bit
{
    [TestFixture]
    public class index_map_should_detect_corruption: SpecificationWithDirectory
    {
        private string _indexMapFileName;
        private string _ptableFileName;
        private PTable _ptable;
        protected byte _ptableVersion = PTableVersions.Index32Bit;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            _indexMapFileName = GetFilePathFor("index.map");
            _ptableFileName = GetFilePathFor("ptable");

            var indexMap = IndexMap.FromFile(_indexMapFileName, _ptableVersion, maxTablesPerLevel: 2);
            var memtable = new HashListMemTable(_ptableVersion, maxSize: 10);
            memtable.Add(0,0,0);
            memtable.Add(1,1,100);
            _ptable = PTable.FromMemtable(memtable, _ptableFileName);

            indexMap = indexMap.AddPTable(_ptable, 0, 0, (streamId, hash) => hash, _ => new Tuple<string, bool>("", true), new GuidFilenameProvider(PathName), _ptableVersion).MergedMap;
            indexMap.SaveToFile(_indexMapFileName);
        }

        [TearDown]
        public override void TearDown()
        {
            if (_ptable != null)
                _ptable.MarkForDestruction();
            base.TearDown();
        }

        [Test]
        public void when_ptable_file_is_deleted()
        {
            _ptable.MarkForDestruction();
            _ptable = null;
            File.Delete(_ptableFileName);

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_indexmap_file_does_not_have_md5_checksum()
        {
            var lines = File.ReadAllLines(_indexMapFileName);
            File.WriteAllLines(_indexMapFileName, lines.Skip(1));

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_indexmap_file_does_not_have_latest_commit_position()
        {
            var lines = File.ReadAllLines(_indexMapFileName);
            File.WriteAllLines(_indexMapFileName, lines.Where((x,i) => i != 1));

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_indexmap_file_exists_but_is_empty()
        {
            File.WriteAllText(_indexMapFileName, "");

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_indexmap_file_is_garbage()
        {
            File.WriteAllText(_indexMapFileName, "alkfjasd;lkf\nasdfasdf\n");

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_checkpoints_pair_is_corrupted()
        {
            using (var fs = File.Open(_indexMapFileName, FileMode.Open))
            {
                fs.Position = 34;
                var b = (byte)fs.ReadByte();
                b ^= 1;
                fs.Position = 34;
                fs.WriteByte(b);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_line_is_missing_one_number()
        {
            var lines = File.ReadAllLines(_indexMapFileName);
            File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], string.Format("0,{0}", _ptableFileName)});

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_line_constists_only_of_filename()
        {
            var lines = File.ReadAllLines(_indexMapFileName);
            File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], _ptableFileName });

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_line_is_missing_filename()
        {
            var lines = File.ReadAllLines(_indexMapFileName);
            File.WriteAllLines(_indexMapFileName, new[] { lines[0], lines[1], "0,0" });

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_indexmap_md5_checksum_is_corrupted()
        {
            using (var fs = File.Open(_indexMapFileName, FileMode.Open))
            {
                var b = (byte)fs.ReadByte();
                b ^= 1; // swap single bit
                fs.Position = 0;
                fs.WriteByte(b);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_hash_is_corrupted()
        {
            _ptable.Dispose();
            _ptable = null;

            using (var fs = File.Open(_ptableFileName, FileMode.Open))
            {
                fs.Seek(-PTable.MD5Size, SeekOrigin.End);
                var b = (byte)fs.ReadByte();
                b ^= 1;
                fs.Seek(-PTable.MD5Size, SeekOrigin.End);
                fs.WriteByte(b);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_type_is_corrupted()
        {
            _ptable.Dispose();
            _ptable = null;

            using (var fs = File.Open(_ptableFileName, FileMode.Open))
            {
                fs.Seek(0, SeekOrigin.Begin);
                fs.WriteByte(123);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_header_is_corrupted()
        {
            _ptable.Dispose();
            _ptable = null;

            using (var fs = File.Open(_ptableFileName, FileMode.Open))
            {
                fs.Position = new Random().Next(0, PTableHeader.Size);
                var b = (byte)fs.ReadByte();
                b ^= 1;
                fs.Position -= 1;
                fs.WriteByte(b);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }

        [Test]
        public void when_ptable_data_is_corrupted()
        {
            _ptable.Dispose();
            _ptable = null;

            using (var fs = File.Open(_ptableFileName, FileMode.Open))
            {
                fs.Position = new Random().Next(PTableHeader.Size, (int)fs.Length);
                var b = (byte)fs.ReadByte();
                b ^= 1;
                fs.Position -= 1;
                fs.WriteByte(b);
            }

            Assert.Throws<CorruptIndexException>(() => IndexMap.FromFile(_indexMapFileName, 2));
        }
    }
}
