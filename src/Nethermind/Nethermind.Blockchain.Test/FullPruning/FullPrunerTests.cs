﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain.FullPruning;
using Nethermind.Blockchain.Processing;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Db;
using Nethermind.Db.FullPruning;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;
using Org.BouncyCastle.Asn1.Cms;

namespace Nethermind.Blockchain.Test.FullPruning
{
    [Parallelizable(ParallelScope.All)]
    public class FullPrunerTests
    {
        [Test]
        public async Task can_prune()
        {
            TestContext test = CreateTest();
            bool contextDisposed = await test.WaitForPruning();
            contextDisposed.Should().BeTrue();
        }

        [Test]
        public async Task pruning_deletes_old_db_on_success()
        {
            TestContext test = CreateTest(clearPrunedDb: true);
            await test.WaitForPruning();
            test.TrieDb.Count.Should().Be(0);
        }
        
        [Test]
        public async Task pruning_keeps_old_db_on_fail()
        {
            TestContext test = CreateTest(false);
            int count = test.TrieDb.Count;
            await test.WaitForPruning();
            test.TrieDb.Count.Should().Be(count);
        }
        
        [Test]
        public async Task pruning_deletes_new_db_on_fail()
        {
            TestContext test = CreateTest(false);
            await test.WaitForPruning();
            test.CopyDb.Count.Should().Be(0);
        }
        
        [Test]
        public async Task pruning_keeps_new_db_on_success()
        {
            TestContext test = CreateTest();
            int count = test.TrieDb.Count;
            bool result = await test.WaitForPruning();
            result.Should().BeTrue();
            test.CopyDb.Count.Should().Be(count);
        }

        [Test]
        public async Task can_not_start_pruning_when_other_is_in_progress()
        {
            TestContext test = CreateTest();
            test.FullPruningDb.CanStartPruning.Should().BeTrue();
            
            test.PruningTrigger.Prune += Raise.Event();
            test.BlockTree.NewHeadBlock += Raise.EventWith(new BlockEventArgs(Build.A.Block.TestObject));
            test.FullPruningDb.CanStartPruning.Should().BeFalse();
            test.FullPruningDb.Context.WaitForFinish.Set();
            await test.FullPruningDb.Context.DisposeEvent.WaitOneAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);
            
            test.FullPruningDb.CanStartPruning.Should().BeTrue();
        }

        [Test]
        public async Task should_not_start_multiple_pruning()
        {
            TestContext test = CreateTest();
            test.PruningTrigger.Prune += Raise.Event();
            await test.WaitForPruning();
            test.FullPruningDb.PruningStarted.Should().Be(1);
        }
        
        [Test]
        public async Task should_duplicate_writes_while_pruning()
        {
            TestContext test = CreateTest();
            test.WaitForPruningStart();
            byte[] key = {0, 1, 2};
            test.FullPruningDb[key] = key;
            test.FullPruningDb.Context.WaitForFinish.Set();
            await test.FullPruningDb.Context.DisposeEvent.WaitOneAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);

            test.FullPruningDb[key].Should().BeEquivalentTo(key);
        }
        
        [Test]
        public async Task should_duplicate_writes_to_batches_while_pruning()
        {
            TestContext test = CreateTest();
            byte[] key = {0, 1, 2};
            TestFullPruningDb.TestPruningContext context = test.WaitForPruningStart();
            
            using (IBatch batch = test.FullPruningDb.StartBatch())
            {
                batch[key] = key;
            }

            await test.WaitForPruningEnd(context);
            
            test.FullPruningDb[key].Should().BeEquivalentTo(key);
        }

        private static TestContext CreateTest(bool successfulPruning = true, bool clearPrunedDb = false) => new(successfulPruning, clearPrunedDb);

        private class TestContext
        {
            private readonly bool _clearPrunedDb;
            private readonly Keccak _stateRoot;
            public TestFullPruningDb FullPruningDb { get; }
            public IPruningTrigger PruningTrigger { get; } = Substitute.For<IPruningTrigger>();
            public IBlockTree BlockTree { get; } = Substitute.For<IBlockTree>();
            public IStateReader StateReader { get; }
            public FullPruner Pruner { get; }
            public MemDb TrieDb { get; }
            public MemDb CopyDb { get; }

            public TestContext(bool successfulPruning, bool clearPrunedDb = false)
            {
                _clearPrunedDb = clearPrunedDb;
                TrieDb = new MemDb();
                CopyDb = new MemDb();
                IRocksDbFactory rocksDbFactory = Substitute.For<IRocksDbFactory>();
                rocksDbFactory.CreateDb(Arg.Any<RocksDbSettings>()).Returns(TrieDb, CopyDb);
                
                PatriciaTree trie = Build.A.Trie(TrieDb).WithAccountsByIndex(0, 100).TestObject;
                _stateRoot = trie.RootHash;
                StateReader = new StateReader(new TrieStore(TrieDb, LimboLogs.Instance), new MemDb(), LimboLogs.Instance);
                FullPruningDb = new TestFullPruningDb(new RocksDbSettings("test", "test"), rocksDbFactory, successfulPruning, clearPrunedDb);
                
                Pruner = new(FullPruningDb, PruningTrigger, BlockTree, StateReader, LimboLogs.Instance);
            }

            public async Task<bool> WaitForPruning()
            {
                TestFullPruningDb.TestPruningContext context = WaitForPruningStart();
                bool result = await WaitForPruningEnd(context);
                if (result && _clearPrunedDb)
                {
                    await FullPruningDb.WaitForClearDb.WaitOneAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);
                }

                return result;
            }

            public Task<bool> WaitForPruningEnd(TestFullPruningDb.TestPruningContext context)
            {
                return context is not null
                    ? context.DisposeEvent.WaitOneAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None)
                    : Task.FromResult(false);
            }

            public TestFullPruningDb.TestPruningContext WaitForPruningStart()
            {
                PruningTrigger.Prune += Raise.Event();
                BlockTree.NewHeadBlock += Raise.EventWith(new BlockEventArgs(Build.A.Block.WithStateRoot(_stateRoot).TestObject));
                TestFullPruningDb.TestPruningContext context = FullPruningDb.Context;
                if (context is not null)
                {
                    context.WaitForFinish.Set();
                }

                return context;
            }
        }
        
        private class TestFullPruningDb : FullPruningDb
        {
            private readonly bool _successfulPruning;
            private readonly bool _clearPrunedDb;

            public TestPruningContext Context { get; set; }
            public int PruningStarted { get; private set; }
            public ManualResetEvent WaitForClearDb { get; } = new(false);
            
            public TestFullPruningDb(RocksDbSettings settings, IRocksDbFactory dbFactory, bool successfulPruning, bool clearPrunedDb = false) 
                : base(settings, dbFactory)
            {
                _successfulPruning = successfulPruning;
                _clearPrunedDb = clearPrunedDb;
            }

            protected override void ClearOldDb(IDb oldDb)
            {
                if (_clearPrunedDb)
                {
                    base.ClearOldDb(oldDb);
                    WaitForClearDb.Set();
                }
            }

            public override bool TryStartPruning(out IPruningContext context)
            {
                if (base.TryStartPruning(out context))
                {
                    context = Context = new TestPruningContext(context, _successfulPruning);
                    PruningStarted++;
                    return true;
                }
                return false;
            }

            internal class TestPruningContext : IPruningContext
            {
                private readonly IPruningContext _context;
                private readonly bool _successfulPruning;

                public ManualResetEvent DisposeEvent { get; } = new(false);
                public ManualResetEvent WaitForFinish { get; } = new(false);
                
                public TestPruningContext(IPruningContext context, bool successfulPruning)
                {
                    _context = context;
                    _successfulPruning = successfulPruning;
                }

                public void Dispose()
                {
                    _context.Dispose();
                    DisposeEvent.Set();
                }

                public byte[] this[byte[] key]
                {
                    set => _context[key] = value;
                }

                public void Commit()
                {
                    WaitForFinish.WaitOne();
                    if (_successfulPruning)
                    {
                        _context.Commit();
                    }
                }

                public void MarkStart()
                {
                    _context.MarkStart();
                }
            }
        }
    }
}