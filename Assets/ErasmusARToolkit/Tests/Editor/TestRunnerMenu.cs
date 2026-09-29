using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Erasmus.ARToolkit.Tests
{
    /// <summary>
    /// Developer helper: runs the toolkit tests and writes a summary to Temp/ErasmusTestResults.txt.
    /// Lives in the Tests folder, so it is not part of the exported package.
    /// </summary>
    static class TestRunnerMenu
    {
        const string ResultsPath = "Temp/ErasmusTestResults.txt";

        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Run Edit Mode Tests", priority = 1000)]
        static void RunEditModeTests() => Run(TestMode.EditMode, "Erasmus.ARToolkit.Tests.Editor", "Erasmus.ARToolkit.Tests.OpenCV");

        [MenuItem("Tools/Erasmus AR Toolkit/Developer/Run Play Mode Tests", priority = 1001)]
        static void RunPlayModeTests() => Run(TestMode.PlayMode, "Erasmus.ARToolkit.Tests.Runtime");

        /// <summary>Play Mode runs reload the domain, so the callback is registered on every load.</summary>
        [InitializeOnLoadMethod]
        static void RegisterResultWriter()
        {
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new ResultWriter());
        }

        static void Run(TestMode mode, params string[] assemblyNames)
        {
            File.WriteAllText(ResultsPath, "RUNNING " + mode);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = mode, assemblyNames = assemblyNames }));
        }

        sealed class ResultWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var summary = new StringBuilder();
                summary.AppendLine($"PASS={result.PassCount} FAIL={result.FailCount} SKIP={result.SkipCount}");
                AppendFailures(result, summary);
                File.WriteAllText(ResultsPath, summary.ToString());
                Debug.Log("[Erasmus AR Toolkit] Tests finished: " + summary);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            static void AppendFailures(ITestResultAdaptor result, StringBuilder summary)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                {
                    summary.AppendLine($"FAILED {result.FullName}: {result.Message}");
                }

                foreach (var child in result.Children)
                {
                    AppendFailures(child, summary);
                }
            }
        }
    }
}
