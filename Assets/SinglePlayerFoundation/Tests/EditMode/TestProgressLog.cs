using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: SPF.Tests.EditMode.TestProgressLog]

namespace SPF.Tests.EditMode
{
    /// <summary>
    /// Logs each test's start/finish and managed memory to stdout and the Unity log, so a run that
    /// the OS kills mid-test still shows which test was running.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class TestProgressLogAttribute : Attribute, ITestAction
    {
        public ActionTargets Targets => ActionTargets.Test;
        public void BeforeTest(ITest test) => Write("start", test);
        public void AfterTest(ITest test) => Write("end  ", test);

        static void Write(string phase, ITest test)
        {
            string line = "[SPF-TEST] " + phase + " " + test.FullName + " managedMB=" + (GC.GetTotalMemory(false) >> 20);
            Console.WriteLine(line);
            Console.Out.Flush();
            UnityEngine.Debug.Log(line);
        }
    }
}
