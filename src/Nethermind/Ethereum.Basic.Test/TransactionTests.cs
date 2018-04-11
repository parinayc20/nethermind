﻿/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Ethereum.Test.Base;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Encoding;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using NUnit.Framework;

namespace Ethereum.Basic.Test
{
    public class TransactionTests
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        }

        public static IEnumerable<TransactionTest> LoadTests()
        {
            return TestLoader.LoadFromFile<TransactionTestJson[], TransactionTest>(
                "txtest.json",
                jsonArray => jsonArray.Select(Convert));
        }

        [TestCaseSource(nameof(LoadTests))]
        public void Test(TransactionTest test)
        {
            EthereumSigner ethereumSigner = new EthereumSigner(OlympicSpecProvider.Instance, NullLogger.Instance);
            Transaction decodedUnsigned = Rlp.Decode<Transaction>(test.Unsigned);
            Assert.AreEqual(test.Value, decodedUnsigned.Value);
            Assert.AreEqual(test.GasPrice, decodedUnsigned.GasPrice);
            Assert.AreEqual(test.StartGas, decodedUnsigned.GasLimit);
            Assert.AreEqual(test.Data, decodedUnsigned.Data ?? decodedUnsigned.Init);
            Assert.AreEqual(test.To, decodedUnsigned.To);
            Assert.AreEqual(test.Nonce, decodedUnsigned.Nonce);

            Transaction decodedSigned = Rlp.Decode<Transaction>(test.Signed);
            ethereumSigner.Sign(test.PrivateKey, decodedUnsigned, 0);
            Assert.AreEqual(decodedSigned.Signature.R, decodedUnsigned.Signature.R);
            BigInteger expectedS = decodedSigned.Signature.S.ToUnsignedBigInteger();
            BigInteger actualS = decodedUnsigned.Signature.S.ToUnsignedBigInteger();
            BigInteger otherS = EthereumSigner.LowSTransform - actualS;

            // test does not use normalized signature
            if (otherS != expectedS && actualS != expectedS)
            {
                throw new Exception("S is wrong");
            }

            int vToCompare = decodedUnsigned.Signature.V;
            if (otherS == decodedSigned.Signature.S.ToUnsignedBigInteger())
            {
                vToCompare = vToCompare == 27 ? 28 : 27;
            }

            Assert.AreEqual(decodedSigned.Signature.V, vToCompare);
        }

        private static TransactionTest Convert(TransactionTestJson testJson)
        {
            TransactionTest test = new TransactionTest();
            test.Value = testJson.Value;
            test.Data = Hex.ToBytes(testJson.Data);
            test.GasPrice = testJson.GasPrice;
            test.PrivateKey = new PrivateKey(testJson.Key);
            test.Nonce = testJson.Nonce;
            test.Signed = new Rlp(Hex.ToBytes(testJson.Signed));
            byte[] unsigned = Hex.ToBytes(testJson.Unsigned);
            if (unsigned[0] == 0xf8)
            {
                unsigned[1] -= 3;
            }
            else
            {
                unsigned[0] -= 3;
            }

            test.Unsigned = new Rlp(unsigned.Slice(0, unsigned.Length - 3));
            test.StartGas = testJson.StartGas;
            test.To = string.IsNullOrEmpty(testJson.To) ? null : new Address(testJson.To);
            return test;
        }

        private class TransactionTestJson
        {
            public string Key { get; set; }
            public long Nonce { get; set; }
            public long GasPrice { get; set; }
            public long StartGas { get; set; }
            public string To { get; set; }
            public long Value { get; set; }
            public string Data { get; set; }
            public string Unsigned { get; set; }
            public string Signed { get; set; }
        }

        [DebuggerDisplay("{PrivateKey}")]
        public class TransactionTest
        {
            public PrivateKey PrivateKey { get; set; }
            public BigInteger Nonce { get; set; }
            public BigInteger GasPrice { get; set; }
            public BigInteger StartGas { get; set; }
            public Address To { get; set; }
            public BigInteger Value { get; set; }
            public byte[] Data { get; set; }
            public Rlp Unsigned { get; set; }
            public Rlp Signed { get; set; }

            public override string ToString()
            {
                return PrivateKey.ToString();
            }
        }
    }
}