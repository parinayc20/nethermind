//  Copyright (c) 2021 Demerzel Solutions Limited
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

using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing
{
    public class BlockCallOutputTracer : IBlockTracer
    {
        private readonly Dictionary<Keccak, CallOutputTracer> _results = new();
        public bool IsTracingRewards => false;
        public void ReportReward(Address author, string rewardType, UInt256 rewardValue) { }
        public void StartNewBlockTrace(Block block) { }
        public ITxTracer StartNewTxTrace(Transaction tx) => _results[tx.Hash!] = new CallOutputTracer();
        public void EndTxTrace() { }
        public IReadOnlyDictionary<Keccak, CallOutputTracer> BuildResults() => _results;
    }
}