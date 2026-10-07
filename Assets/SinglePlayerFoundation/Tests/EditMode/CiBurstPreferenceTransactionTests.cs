using System;
using System.Collections.Generic;
using NUnit.Framework;
using SPF.Editor;
using Value = SPF.Editor.CiBurstPreferenceTransaction.Value;

namespace SPF.Tests.EditMode
{
    public class CiBurstPreferenceTransactionTests
    {
        sealed class FakeStore : CiBurstPreferenceTransaction.IStore
        {
            public readonly Dictionary<string, Value> Values = new Dictionary<string, Value>();
            public Value Session;
            public bool Enabled, Backend = true, ThrowAfterActivation;
            public int Accesses, SetterCalls;
            public Value Read(string key) { Accesses++; return Values.TryGetValue(key, out var value) ? value : new Value(false, false); }
            public void Write(string key, Value value) { Accesses++; Values[key] = value; }
            public Value SafetySession { get { Accesses++; return Session; } set { Accesses++; Session = value; } }
            public bool CompilationEnabled
            {
                get { Accesses++; return Enabled; }
                set
                {
                    Accesses++; SetterCalls++; Enabled = value;
                    // Simulate the real Burst global-options save callback, including absent keys.
                    foreach (string key in CiBurstPreferenceTransaction.PreferenceKeys) Write(key, new Value(true, value));
                    Session = new Value(true, true);
                    if (value && ThrowAfterActivation) throw new InvalidOperationException("Injected failure after side effects");
                }
            }
            public bool BackendEnabled => Backend && Enabled;
        }

        static Value State(int state) => new Value(state != 0, state == 2);
        static void Activate(FakeStore store) => CiBurstPreferenceTransaction.Enable(true,
            new[] { CiBurstPreferenceTransaction.ApprovalFlag }, store);

        [Test]
        public void PreservesEveryNonTargetPreferenceAndSessionStateIncludingAbsence()
        {
            // All absent/false/true combinations for all five prefs and the session value.
            for (int combination = 0; combination < 729; combination++)
            {
                var store = new FakeStore(); int remaining = combination;
                foreach (string key in CiBurstPreferenceTransaction.PreferenceKeys)
                { store.Values[key] = State(remaining % 3); remaining /= 3; }
                store.Session = State(remaining % 3);
                var expected = new Dictionary<string, Value>(store.Values); Value session = store.Session;
                Activate(store);
                Assert.IsTrue(store.Enabled);
                Assert.IsTrue(store.Read("BurstCompilation").Matches(new Value(true, true)));
                for (int i = 1; i < CiBurstPreferenceTransaction.PreferenceKeys.Length; i++)
                {
                    string key = CiBurstPreferenceTransaction.PreferenceKeys[i];
                    Assert.IsTrue(store.Read(key).Matches(expected[key]), key + " combination " + combination);
                }
                Assert.IsTrue(store.Session.Matches(session), "session combination " + combination);
            }
        }

        [TestCase(false, "--spf-enable-approved-ci-burst-20261007")]
        [TestCase(true, "--spf-enable-approved-ci-burst")]
        [TestCase(true, "--spf-enable-approved-ci-burst-20261007=true")]
        [TestCase(true, "")]
        public void MissingExactAuthorizationNeverTouchesStore(bool batch, string argument)
        {
            var store = new FakeStore();
            Assert.Throws<InvalidOperationException>(() => CiBurstPreferenceTransaction.Enable(batch, new[] { argument }, store));
            Assert.AreEqual(0, store.Accesses);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FailedActivationRestoresAllPreferencesSessionAndOption(bool setterThrows)
        {
            var store = new FakeStore { ThrowAfterActivation = setterThrows, Backend = setterThrows };
            for (int i = 0; i < CiBurstPreferenceTransaction.PreferenceKeys.Length; i++)
                store.Values[CiBurstPreferenceTransaction.PreferenceKeys[i]] = State(i % 3);
            store.Session = State(0);
            var expected = new Dictionary<string, Value>(store.Values);
            Assert.Throws<InvalidOperationException>(() => Activate(store));
            Assert.IsFalse(store.Enabled);
            foreach (var item in expected) Assert.IsTrue(store.Read(item.Key).Matches(item.Value), item.Key);
            Assert.IsFalse(store.Session.Exists);
        }

        [Test]
        public void AlreadyEnabledOptionDoesNotBroadcastAnotherOptionsChange()
        {
            var store = new FakeStore { Enabled = true };
            Activate(store);
            Assert.AreEqual(0, store.SetterCalls);
            Assert.IsTrue(store.Read("BurstCompilation").Matches(new Value(true, true)));
        }
    }
}
