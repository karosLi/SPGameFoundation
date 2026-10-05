using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace SPF.Shell.Performance
{
    /// <summary>Device heat as the OS reports it (iOS ProcessInfo.thermalState, Android PowerManager thermal status).</summary>
    public enum ThermalLevel : byte { Unknown, Nominal, Fair, Serious, Critical }

    /// <summary>A snapshot of what the device says about heat and power.</summary>
    public struct DeviceState
    {
        public ThermalLevel Thermal;
        public bool LowPowerMode;     // iOS Low Power Mode / Android battery saver
        public float Battery;         // 0..1, negative when unknown
        public bool Charging;

        /// <summary>Quality level the governor must not go below in this state.</summary>
        public int QualityFloor(int maxLevel)
        {
            int floor = Thermal switch
            {
                ThermalLevel.Fair => 1,
                ThermalLevel.Serious => 2,
                ThermalLevel.Critical => maxLevel,
                _ => 0,
            };
            if (LowPowerMode || !Charging && Battery >= 0f && Battery < 0.15f) floor = Math.Max(floor, 1);
            return Math.Min(floor, maxLevel);
        }

        /// <summary>Frame-rate cap for this state (0 = none): hot or saving power → 30 FPS.</summary>
        public int FrameRateCap => Thermal >= ThermalLevel.Serious || LowPowerMode ? 30 : 0;
    }

    /// <summary>
    /// Polls the OS for thermal state and power saving every few seconds, without the Adaptive Performance package:
    /// a tiny iOS plugin (Plugins/iOS/SPFDeviceState.mm) and Android's PowerManager through JNI (API 29+ for
    /// thermal status; older devices report Unknown and the governor falls back to frame-time inference).
    /// Polling allocates on Android (JNI argument arrays) a few bytes every <see cref="IntervalSeconds"/>, never per frame.
    /// </summary>
    public sealed class ThermalMonitor
    {
        public float IntervalSeconds = 3f;
        /// <summary>Tests and the editor can force a state.</summary>
        public DeviceState? Override;

        float m_Next = float.NegativeInfinity;
        DeviceState m_State = new DeviceState { Thermal = ThermalLevel.Unknown, Battery = -1f, Charging = true };

        public DeviceState State => Override ?? m_State;

        /// <summary>Re-reads the device state if the interval elapsed; returns true when it changed.</summary>
        public bool Poll(float now)
        {
            if (Override.HasValue || now < m_Next) return false;
            m_Next = now + IntervalSeconds;
            var s = Read();
            bool changed = s.Thermal != m_State.Thermal || s.LowPowerMode != m_State.LowPowerMode || s.Charging != m_State.Charging
                || (s.Battery < 0.15f) != (m_State.Battery < 0.15f);
            m_State = s;
            return changed;
        }

        static DeviceState Read()
        {
            var s = new DeviceState
            {
                Thermal = ThermalLevel.Unknown,
                Battery = SystemInfo.batteryLevel,
                Charging = SystemInfo.batteryStatus != BatteryStatus.Discharging,
            };
#if UNITY_IOS && !UNITY_EDITOR
            s.Thermal = (ThermalLevel)(SPF_ThermalState() + 1);
            s.LowPowerMode = SPF_LowPowerMode() != 0;
#elif UNITY_ANDROID && !UNITY_EDITOR
            ReadAndroid(ref s);
#endif
            return s;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int SPF_ThermalState();
        [DllImport("__Internal")] static extern int SPF_LowPowerMode();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject s_Power;
        static bool s_PowerFailed;

        static void ReadAndroid(ref DeviceState s)
        {
            if (s_PowerFailed) return;
            try
            {
                if (s_Power == null)
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    s_Power = activity.Call<AndroidJavaObject>("getSystemService", "power");
                }
                s.LowPowerMode = s_Power.Call<bool>("isPowerSaveMode");
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") >= 29)
                {
                    // THERMAL_STATUS_NONE 0, LIGHT 1, MODERATE 2, SEVERE 3, CRITICAL 4, EMERGENCY 5, SHUTDOWN 6
                    int status = s_Power.Call<int>("getCurrentThermalStatus");
                    s.Thermal = status <= 0 ? ThermalLevel.Nominal : status == 1 ? ThermalLevel.Fair : status <= 3 ? ThermalLevel.Serious : ThermalLevel.Critical;
                }
            }
            catch (Exception e)
            {
                s_PowerFailed = true;
                Debug.LogWarning("SPF thermal monitor unavailable: " + e.Message);
            }
        }
#endif
    }
}
