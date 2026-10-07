using System;
using Unity.Burst;
using UnityEngine;

namespace Latios2022Lab
{
    // Runtime assembly: PlayMode and player code must not depend on UnityEditor tools.
    public static class LabRunPolicy
    {
        public const string EditorVersion = "2022.3.62f2";
        public static bool ExpectedBurst
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("LATIOS_LAB_EXPECT_BURST");
                if (string.IsNullOrEmpty(value) || value == "on") return true;
                if (value == "off") return false;
                throw new InvalidOperationException("LATIOS_LAB_EXPECT_BURST must be on or off.");
            }
        }

        public static bool VerifyRequestedMode()
        {
            if (Application.unityVersion != EditorVersion)
                throw new InvalidOperationException("Use exactly Unity " + EditorVersion);
#if UNITY_EDITOR && !ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new InvalidOperationException("Native collection safety checks must be enabled in the Editor.");
#endif
#if !ENTITY_STORE_V1 || !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
            throw new InvalidOperationException("The pinned Latios 0.11.5 defines are missing.");
#endif
            bool expected = ExpectedBurst;
            if (BurstCompiler.IsEnabled != expected)
                throw new InvalidOperationException("The requested Burst control is unavailable; do not change persistent preferences automatically.");
#if UNITY_EDITOR
            if (expected && !BurstCompiler.Options.EnableBurstSafetyChecks)
                throw new InvalidOperationException("Burst safety checks are disabled; do not weaken the gate.");
#endif
            // Runtime development/AOT safety instrumentation is a separate build/device gate.
            return expected;
        }
    }
}
