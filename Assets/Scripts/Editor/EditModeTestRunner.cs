using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class EditModeTestRunner
{
    public const string ResultsPath = "Temp/edit-mode-test-results.json";

    public static void Run()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new ResultCollector());
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        Debug.Log("[EditModeTestRunner] Run started; results will be written to " + ResultsPath);
    }

    /// <summary>Run only the EditMode tests whose full name matches <paramref name="groupRegex"/>
    /// (e.g. "BulletReactionRoutingTests"). Iterate on one fixture without the whole suite.</summary>
    public static void RunFiltered(string groupRegex)
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new ResultCollector());
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            groupNames = new[] { groupRegex },
        }));
        Debug.Log($"[EditModeTestRunner] Filtered run started ({groupRegex}); results -> " + ResultsPath);
    }

    /// <summary>
    /// Runs one fixture's [Test] methods right here, by reflection, and returns a report. The Test
    /// Runner asks to save the open scenes first, which blocks a run from the CLI:
    /// <c>unity command script eval 'return EditModeTestRunner.RunInline("ChestPlacementTests");'</c>.
    /// Plain [Test] methods with [SetUp]/[TearDown] only: no [TestCase] or [UnityTest].
    /// </summary>
    public static string RunInline(string fixtureTypeName)
    {
        var type = System.Type.GetType(fixtureTypeName + ", Assembly-CSharp-Editor");
        if (type == null) return fixtureTypeName + " not found in Assembly-CSharp-Editor";

        System.Reflection.MethodInfo setUp = null, tearDown = null;
        var tests = new System.Collections.Generic.List<System.Reflection.MethodInfo>();
        foreach (var method in type.GetMethods())
        {
            if (method.IsDefined(typeof(NUnit.Framework.SetUpAttribute), false)) setUp = method;
            else if (method.IsDefined(typeof(NUnit.Framework.TearDownAttribute), false)) tearDown = method;
            else if (method.IsDefined(typeof(NUnit.Framework.TestAttribute), false)) tests.Add(method);
        }

        var report = new StringBuilder();
        int passed = 0;
        foreach (var test in tests)
        {
            var fixture = System.Activator.CreateInstance(type);
            try
            {
                setUp?.Invoke(fixture, null);
                try
                {
                    test.Invoke(fixture, null);
                    passed++;
                    report.AppendLine("PASS " + test.Name);
                }
                finally
                {
                    tearDown?.Invoke(fixture, null);
                }
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                report.AppendLine("FAIL " + test.Name + ": " + e.InnerException?.Message.Trim());
            }
        }

        return $"{passed}/{tests.Count} passed\n" + report;
    }

    private sealed class ResultCollector : ICallbacks
    {
        private int _passed;
        private int _failed;
        private int _skipped;
        private readonly StringBuilder _failures = new();

        public void RunStarted(ITestAdaptor _) { }

        public void TestStarted(ITestAdaptor _) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.HasChildren) return;
            switch (result.TestStatus)
            {
                case TestStatus.Passed: _passed++; break;
                case TestStatus.Failed:
                case TestStatus.Inconclusive:
                    _failed++;
                    _failures.Append("{\"name\":").Append(Quote(result.FullName))
                        .Append(",\"message\":").Append(Quote(result.Message ?? ""))
                        .Append(",\"stack\":").Append(Quote(result.StackTrace ?? ""))
                        .Append("},");
                    break;
                case TestStatus.Skipped: _skipped++; break;
            }
        }

        public void RunFinished(ITestResultAdaptor _)
        {
            var failuresJson = _failures.Length > 0
                ? "[" + _failures.ToString().TrimEnd(',') + "]"
                : "[]";
            var json = $"{{\"passed\":{_passed},\"failed\":{_failed},\"skipped\":{_skipped},\"failures\":{failuresJson}}}";
            File.WriteAllText(ResultsPath, json);
            Debug.Log($"[EditModeTestRunner] Done. passed={_passed} failed={_failed} skipped={_skipped}");
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
