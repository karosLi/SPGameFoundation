using System;
using System.IO;
using Unity.Burst;
using UnityEngine;

namespace Latios2022Lab
{
    public sealed class LabPlayerSmoke : MonoBehaviour
    {
        private void Start()
        {
            var result = new SmokeResult {
                editor = Application.unityVersion,
                platform = Application.platform.ToString(),
                cpu = SystemInfo.processorType,
                burst = BurstCompiler.IsEnabled,
                passed = false,
                message = "Started"
            };
            try
            {
                if (!BurstCompiler.IsEnabled) throw new InvalidOperationException("The IL2CPP smoke requires Burst enabled.");
                for (int i = 0; i < 3; i++) CollectionProbe.Run(8, CollectionExit.DisposeWorld);
                for (int i = 0; i < 4; i++) PsyshockProbe.RunPairs(i, LayerExecution.Parallel, 2);
                PsyshockProbe.RunQueries(true);
                result.passed = true;
                result.message = "Core collection chain and Psyshock array/query smoke passed.";
            }
            catch (Exception exception)
            {
                result.message = exception.ToString();
                Debug.LogException(exception);
            }
            string json = JsonUtility.ToJson(result, true);
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "latios-s1a-smoke.json"), json);
            Debug.Log("LATIOS_S1A_SMOKE " + json);
            Application.Quit(result.passed ? 0 : 1);
        }

        [Serializable] private class SmokeResult
        {
            public string editor, platform, cpu, message;
            public bool burst, passed;
        }
    }
}
