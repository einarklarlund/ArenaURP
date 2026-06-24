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
