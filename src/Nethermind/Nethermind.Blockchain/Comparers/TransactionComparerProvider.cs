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

using System.Collections.Generic;
using Nethermind.Blockchain.Find;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.TxPool;

namespace Nethermind.Blockchain.Comparers
{
    public class TransactionComparerProvider : ITransactionComparerProvider
    {
        private readonly ISpecProvider _specProvider;
        private readonly IBlockFinder _blockFinder;
        
        // we're caching default comparer
        private IComparer<WrappedTransaction>? _defaultComparer = null;

        public TransactionComparerProvider(ISpecProvider specProvider, IBlockFinder blockFinder)
        {
            _specProvider = specProvider;
            _blockFinder = blockFinder;
        }

        public IComparer<WrappedTransaction> GetDefaultComparer()
        {
            if (_defaultComparer == null)
            {
                IComparer<WrappedTransaction> gasPriceComparer = new GasPriceTxComparer(_blockFinder, _specProvider);
                _defaultComparer = gasPriceComparer
                    .ThenBy(CompareTxByTimestamp.Instance)
                    .ThenBy(CompareTxByPoolIndex.Instance)
                    .ThenBy(CompareTxByGasLimit.Instance);
            }

            return _defaultComparer;
        }

        public IComparer<WrappedTransaction> GetDefaultProducerComparer(BlockPreparationContext blockPreparationContext)
        {
                IComparer<WrappedTransaction> gasPriceComparer =
                    new GasPriceTxComparerForProducer(blockPreparationContext, _specProvider);
                return gasPriceComparer
                .ThenBy(CompareTxByTimestamp.Instance)
                .ThenBy(CompareTxByPoolIndex.Instance)
                .ThenBy(CompareTxByGasLimit.Instance);
        }
    }
}
