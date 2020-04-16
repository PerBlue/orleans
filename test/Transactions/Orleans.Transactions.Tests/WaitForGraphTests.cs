using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Orleans.Runtime;
using Orleans.Transactions.DeadlockDetection;
using Xunit;
using Xunit.Abstractions;

namespace Orleans.Transactions.Tests
{
    [TestCategory("BVT"), TestCategory("Transactions")]
    public class WaitForGraphTests
    {
        private ITestOutputHelper output;

        public WaitForGraphTests(ITestOutputHelper output)
        {
            this.output = output;
            keyByTx.Clear();
            txByKey.Clear();
        }

        private static readonly IDictionary<string, Guid> txByKey = new Dictionary<string, Guid>();
        private static readonly IDictionary<Guid, string> keyByTx = new Dictionary<Guid, string>();
        // create a very fake participant id - we only need it to be unique up to k
        private static ParticipantId Res(string k) => new ParticipantId(k, null, ParticipantId.Role.Resource);
        private static Guid Tx(string k)
        {
            if (txByKey.TryGetValue(k, out var id))
            {
                return id;
            }
            id = Guid.NewGuid();
            txByKey[k] = id;
            keyByTx[id] = k;
            return id;
        }

        private static string Key(Guid tx) => keyByTx.TryGetValue(tx, out var k) ? k : "NA";
        private static string Key(ParticipantId res) => res.Name;
        private static LockInfo Lock(string tx, string res) => LockInfo.ForLock(Res(res), Tx(tx));
        private static LockInfo Wait(string tx, string res) => LockInfo.ForWait(Res(res), Tx(tx));

        private static string FormatCycle(IEnumerable<LockInfo> cycle) => string.Join(",", cycle.Select(FormatLock));

        private static string FormatLock(LockInfo lockInfo) =>
            lockInfo.IsWait
                ? $"T{Key(lockInfo.TxId)}->R{Key(lockInfo.Resource)}"
                : $"R{Key(lockInfo.Resource)}->T{Key(lockInfo.TxId)}";

        private static void AssertSameLocks(IEnumerable<LockInfo> a, IEnumerable<LockInfo> b)
        {
            var sa = new HashSet<LockInfo>(a);
            var sb = new HashSet<LockInfo>(b);
            if (!sa.SetEquals(sb))
            {
                Assert.False(true, $"expected {FormatCycle(sa)} to equal {FormatCycle(sb)}");
            }
        }

        [Fact]
        public void BasicConstruction()
        {
            var locks = new[] {Lock("0", "a"), Wait("1", "a"), Lock("1", "b"), Wait("0", "b")};
            var wfg = new WaitForGraph(locks);
            AssertSameLocks(wfg.ToLockKeys(), locks);
            var found = wfg.DetectCycles(out var cycle);
            Assert.True(found);
            this.output.WriteLine(FormatCycle(cycle));
        }

        [Fact]
        public void DisjointMerge()
        {
            var first = new WaitForGraph(new[] {Lock("0", "a"), Wait("1", "a"), Lock("1", "b"), Wait("0", "b")});
            var second = new WaitForGraph(new[] {Lock("2", "c"), Wait("3", "c"), Lock("3", "d"), Wait("2", "d")});
            var changed = first.MergeWith(second, out var full);
            Assert.True(changed);

            var sub1 = full.GetConnectedSubGraph(new[] {Tx("0")}, Enumerable.Empty<ParticipantId>());
            var sub2 = full.GetConnectedSubGraph(new[] {Tx("2")}, Enumerable.Empty<ParticipantId>());

            var locks1 = new HashSet<LockInfo>(sub1.ToLockKeys());
            var locks2 = new HashSet<LockInfo>(sub2.ToLockKeys());

            this.output.WriteLine($"full={FormatCycle(full.ToLockKeys())}");
            this.output.WriteLine($"sub1={FormatCycle(sub1.ToLockKeys())}");
            this.output.WriteLine($"sub2={FormatCycle(sub2.ToLockKeys())}");
            this.output.WriteLine($"first={FormatCycle(first.ToLockKeys())}");
            this.output.WriteLine($"second={FormatCycle(second.ToLockKeys())}");

            Assert.True(locks1.Equals(new HashSet<LockInfo>(sub1.ToLockKeys())));
            Assert.True(locks2.Equals(new HashSet<LockInfo>(sub2.ToLockKeys())));
        }


    }
}