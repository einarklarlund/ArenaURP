using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Arena.PlayTests
{
    /// <summary>
    /// Owns the lifetime of a locally-launched signal server for a PlayMode fixture: starts
    /// <c>node SignalServer.js</c> from the sibling ArenaInfra checkout, reports when the port is
    /// accepting, and reaps the process on <see cref="Dispose"/> — which the fixture calls from a
    /// <c>finally</c>, so a failing test cannot leave a listener holding the port for the next run.
    ///
    /// A pid file covers the one path <see cref="Dispose"/> cannot: a hard editor crash. The next
    /// <see cref="Launch"/> reaps the orphan it recorded rather than colliding with it. It only ever
    /// kills a pid this class wrote, so a signal server the developer started by hand is never touched.
    ///
    /// Each launch takes an ephemeral port, so two harnesses can run at once and neither collides
    /// with a signal server the developer already has bound to the default. <see cref="Port"/> and
    /// <see cref="SignalAddress"/> are the single source both sides read: the server gets the port
    /// through <c>SIGNAL_PORT</c>, the game gets the address through <c>ARENA_SIGNAL_ADDRESS</c>.
    /// </summary>
    sealed class SignalServerProcess : IDisposable
    {
        const int KillTimeoutMs = 5000;
        const int MaxCapturedOutputChars = 8 * 1024;

        Process _process;
        readonly StringBuilder _output = new StringBuilder();
        readonly object _outputLock = new object();

        SignalServerProcess(Process process, int port)
        {
            _process = process;
            Port = port;
        }

        /// <summary>The port this instance was launched on — never the default, so runs never collide.</summary>
        public int Port { get; }

        /// <summary>What <c>SignalManager</c> must dial to reach this instance.</summary>
        public string SignalAddress => $"ws://localhost:{Port}/Signal";

        /// <summary>Path of the pid file recording the server we started, so a crashed run is reapable.</summary>
        static string PidFilePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ice-harness-signal-server.pid"));

        public bool HasExited
        {
            get
            {
                try { return _process == null || _process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        /// <summary>True once the server is accepting TCP connections on <see cref="Port"/>.</summary>
        public bool IsListening
        {
            get
            {
                if (HasExited) return false;
                try
                {
                    using var probe = new TcpClient();
                    probe.Connect("127.0.0.1", Port);
                    return true;
                }
                catch (SocketException) { return false; }
            }
        }

        /// <summary>Everything the server wrote to stdout/stderr, capped — quoted back on failure.</summary>
        public string CapturedOutput
        {
            get { lock (_outputLock) return _output.ToString(); }
        }

        public static SignalServerProcess Launch(string serverDirectory)
        {
            ReapOrphanFromPreviousRun();

            int port = ReserveEphemeralPort();

            var startInfo = new ProcessStartInfo(ResolveNodeExecutable(), "SignalServer.js")
            {
                WorkingDirectory = serverDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.EnvironmentVariables["SIGNAL_PORT"] = port.ToString();

            var process = Process.Start(startInfo);
            var launched = new SignalServerProcess(process, port);

            process.OutputDataReceived += (_, e) => launched.Capture(e.Data);
            process.ErrorDataReceived += (_, e) => launched.Capture(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            RecordPid(process.Id);
            return launched;
        }

        public void Dispose()
        {
            try
            {
                if (!HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(KillTimeoutMs);
                }
            }
            catch (Exception e)
            {
                // A warning, not an error: teardown must never be the thing that fails a run.
                Debug.LogWarning($"[IceHarness] Could not reap the signal server: {e.Message}");
            }
            finally
            {
                _process?.Dispose();
                _process = null;
                DeletePidFile();
            }
        }

        /// <summary>
        /// A port the OS just confirmed is free. Bind-then-release races nothing in practice: the
        /// server claims it milliseconds later, and two concurrent harnesses are handed different ones.
        /// </summary>
        static int ReserveEphemeralPort()
        {
            var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        void Capture(string line)
        {
            if (line == null) return;
            lock (_outputLock)
            {
                if (_output.Length >= MaxCapturedOutputChars) return;
                _output.AppendLine(line);
            }
        }

        /// <summary>
        /// The server's uWebSockets.js build ships prebuilt binaries only up to Node 20's ABI, so a
        /// machine whose default `node` is newer cannot run it. Prefer an explicit override, then a
        /// Node 20 install, then whatever is on the usual paths.
        /// </summary>
        static string ResolveNodeExecutable()
        {
            var explicitPath = Environment.GetEnvironmentVariable("ARENA_SIGNAL_NODE");
            if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

            var nvmNode20 = NewestNvmNode20();
            if (nvmNode20 != null) return nvmNode20;

            var onUsualPaths = new[] { "/opt/homebrew/bin/node", "/usr/local/bin/node", "/usr/bin/node" }
                .FirstOrDefault(File.Exists);

            return onUsualPaths ?? "node";
        }

        static string NewestNvmNode20()
        {
            try
            {
                var versionsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm", "versions", "node");
                if (!Directory.Exists(versionsDirectory)) return null;

                return Directory.GetDirectories(versionsDirectory, "v20.*")
                    .OrderBy(directory => directory, StringComparer.Ordinal)
                    .Select(directory => Path.Combine(directory, "bin", "node"))
                    .LastOrDefault(File.Exists);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void RecordPid(int pid)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PidFilePath));
                File.WriteAllText(PidFilePath, pid.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IceHarness] Could not record the signal server pid: {e.Message}");
            }
        }

        static void DeletePidFile()
        {
            try { if (File.Exists(PidFilePath)) File.Delete(PidFilePath); }
            catch (Exception) { /* best effort */ }
        }

        static void ReapOrphanFromPreviousRun()
        {
            if (!File.Exists(PidFilePath)) return;

            try
            {
                if (int.TryParse(File.ReadAllText(PidFilePath).Trim(), out int pid))
                {
                    var orphan = Process.GetProcessById(pid); // throws once the pid is gone
                    if (orphan.ProcessName.IndexOf("node", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Debug.LogWarning($"[IceHarness] Reaping a signal server left behind by an earlier run (pid {pid}).");
                        orphan.Kill();
                        orphan.WaitForExit(KillTimeoutMs);
                    }
                }
            }
            catch (ArgumentException) { /* the pid is already gone */ }
            catch (Exception e)
            {
                Debug.LogWarning($"[IceHarness] Could not reap a stale signal server: {e.Message}");
            }
            finally
            {
                DeletePidFile();
            }
        }
    }
}
