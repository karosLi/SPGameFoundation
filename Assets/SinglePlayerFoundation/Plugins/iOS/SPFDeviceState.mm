// Thermal state and Low Power Mode for SPF.Shell.Performance.ThermalMonitor (iOS 11+).
#import <Foundation/Foundation.h>

extern "C"
{
    // 0 nominal, 1 fair, 2 serious, 3 critical
    int SPF_ThermalState()
    {
        return (int)[[NSProcessInfo processInfo] thermalState];
    }

    int SPF_LowPowerMode()
    {
        return [[NSProcessInfo processInfo] isLowPowerModeEnabled] ? 1 : 0;
    }
}
