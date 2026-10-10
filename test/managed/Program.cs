using System;
using System.IO;
using System.Threading;
using NavalCommand.SeaPower.Mod;
using NavalCommand.SeaPower.Harness;

static class Program
{
    private static int assertions;
    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new Exception(description);
        }
        assertions++;
        Console.WriteLine("PASS " + description);
    }

    private static void Reject(Action action, string description)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            Check(true, description);
            return;
        }
        throw new Exception("Expected rejection: " + description);
    }

    private static void Wait(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Worker test timed out");
        }
    }

    private static void Main(string[] arguments)
    {
        string directory = Path.Combine(Path.GetTempPath(), "naval-runtime-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string previousEnabled = Environment.GetEnvironmentVariable("NC_MOD_ENABLED");
        string previousConfig = Environment.GetEnvironmentVariable("NC_MOD_CONFIG");
        string previousLogs = Environment.GetEnvironmentVariable("NC_MOD_LOG_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("NC_MOD_ENABLED", null);
            Environment.SetEnvironmentVariable("NC_MOD_CONFIG", null);
            Environment.SetEnvironmentVariable("NC_MOD_LOG_DIRECTORY", null);
            string validJson = "{\"schema\":1,\"abi\":1,\"enabledByDefault\":false,\"endpoint\":\"127.0.0.1:50051\",\"clientAutoLaunch\":false,\"maxMessageBytes\":1048576,\"maxQueuedOrders\":64,\"maxRetainedRequests\":128}";
            string configPath = Path.Combine(directory, "config.json");
            File.WriteAllText(configPath, validJson);
            var config = RuntimeConfiguration.Load(directory);
            Check(!config.Enabled, "Default-disabled configuration");
            Check(config.NativeLibrary == Path.Combine(directory, "SeaPower.Host.dll"), "Package-relative native path");
            Check(Path.IsPathRooted(config.LogDirectory), "Independent absolute production log directory");
            Environment.SetEnvironmentVariable("NC_MOD_ENABLED", "1");
            Check(RuntimeConfiguration.Load(directory).Enabled, "Explicit activation override");
            Environment.SetEnvironmentVariable("NC_MOD_ENABLED", "yes");
            Reject(() => RuntimeConfiguration.Load(directory), "Invalid activation override");
            Environment.SetEnvironmentVariable("NC_MOD_ENABLED", null);
            foreach (string invalidJson in new[] {
                validJson.Replace("127.0.0.1", "0.0.0.0"),
                validJson.Replace("50051", "65536"),
                validJson.Replace("\"schema\":1", "\"schema\":2"),
                validJson.Replace("\"abi\":1", "\"abi\":2"),
                validJson.Replace("\"maxQueuedOrders\":64", "\"maxQueuedOrders\":100"),
                validJson.Replace("\"clientAutoLaunch\":false", "\"clientAutoLaunch\":true"),
                "{}", "invalid-json"
            })
            {
                File.WriteAllText(configPath, invalidJson);
                Reject(() => RuntimeConfiguration.Load(directory), "Invalid runtime configuration");
            }
            File.WriteAllText(configPath, validJson);
            Environment.SetEnvironmentVariable("NC_MOD_CONFIG", "relative.json");
            Reject(() => RuntimeConfiguration.Load(directory), "Relative explicit config path");
            Environment.SetEnvironmentVariable("NC_MOD_CONFIG", Path.Combine(directory, "missing.json"));
            Reject(() => RuntimeConfiguration.Load(directory), "Missing explicit config");
            Environment.SetEnvironmentVariable("NC_MOD_CONFIG", null);
            Reject(() => new NativeTransport("relative.dll"), "Relative native library");
            Reject(() => new NativeTransport(Path.Combine(directory, "missing.dll")), "Missing native DLL");
            if (arguments.Length == 2)
            {
                try
                {
                    new NativeTransport(arguments[0]);
                    throw new Exception("Expected ABI rejection");
                }
                catch (InvalidOperationException exception)
                {
                    Check(exception.Message == "abi_mismatch", "Loaded native DLL with wrong ABI rejected");
                }
                try
                {
                    new NativeTransport(arguments[1]);
                    throw new Exception("Expected missing export rejection");
                }
                catch (MissingMethodException exception)
                {
                    Check(exception.Message.Contains("nc_initialize"), "Loaded native DLL with missing exports rejected");
                }
            }

            config.LogDirectory = Path.Combine(directory, "worker");
            Directory.CreateDirectory(config.LogDirectory);
            var mailbox = new RuntimeMailbox();
            var transport = new FakeTransport();
            mailbox.Views.Enqueue(new byte[] { 1 });
            mailbox.Results.Enqueue(new byte[] { 2 });
            var worker = new Thread(() => TransportWorker.Run(mailbox, config, _ => transport));
            worker.Start();
            Wait(() => Volatile.Read(ref transport.Views) >= 2 && Volatile.Read(ref transport.Results) >= 2 && mailbox.Commands.Count == 1);
            Check(!mailbox.Failed, "Retryable publish backpressure preserves pending bytes");
            Check(mailbox.Commands.TryDequeue(out byte[] command) && command[0] == 3, "Worker copies native command bytes");
            mailbox.Stop = true;
            Wait(() => mailbox.Completed);
            Check(worker.Join(1000) && transport.Stops == 1, "Orderly off-thread shutdown happens once");
            Check(File.ReadAllText(Path.Combine(config.LogDirectory, "gateway.log")).Contains("worker-stop status=0"), "Shutdown status logged");

            var failedMailbox = new RuntimeMailbox();
            config.LogDirectory = Path.Combine(directory, "failed");
            Directory.CreateDirectory(config.LogDirectory);
            TransportWorker.Run(failedMailbox, config, _ => throw new InvalidOperationException("abi_mismatch"));
            Check(failedMailbox.Failed && failedMailbox.Completed && failedMailbox.Error.Contains("abi_mismatch"), "Transport startup failure is observable");
            config.LogDirectory = configPath;
            var logFailure = new RuntimeMailbox();
            TransportWorker.Run(logFailure, config, _ => transport);
            Check(logFailure.Failed && logFailure.Completed && logFailure.Error.Contains("Diagnostic file failure"), "Unwritable log path retains both failures");

            string evidencePath = Path.Combine(directory, "evidence");
            string token = Guid.NewGuid().ToString();
            var evidence = new EvidenceWriter(evidencePath, token, 123);
            evidence.Emit("playable-ready side=SelectedPlayerSide");
            Wait(() => File.Exists(Path.Combine(evidencePath, "ready.json")));
            Check(File.ReadAllText(Path.Combine(evidencePath, "ready.json")).Contains(token), "Harness-only token/PID readiness receipt");
            evidence.Stop();
            var failedEvidence = new EvidenceWriter(configPath, token, 123);
            Wait(() => failedEvidence.Failed);
            Check(failedEvidence.Error.Contains("Receipt write failed"), "Harness reports unwritable evidence path");
            failedEvidence.Stop();
            Console.WriteLine("Runtime/harness checks passed: " + assertions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NC_MOD_ENABLED", previousEnabled);
            Environment.SetEnvironmentVariable("NC_MOD_CONFIG", previousConfig);
            Environment.SetEnvironmentVariable("NC_MOD_LOG_DIRECTORY", previousLogs);
        }
    }

    private sealed class FakeTransport : INativeTransport
    {
        public int Views;
        public int Results;
        public int Stops;
        private bool sent;
        public int Start(string endpoint) => endpoint == "127.0.0.1:50051" ? 0 : -1;
        public int PublishView(byte[] bytes) => Interlocked.Increment(ref Views) == 1 ? 1 : 0;
        public int PublishResult(byte[] bytes) => Interlocked.Increment(ref Results) == 1 ? 1 : 0;
        public int PollOrder(byte[] buffer, out uint size)
        {
            size = 0;
            if (sent)
            {
                return 1;
            }
            buffer[0] = 3;
            size = 1;
            sent = true;
            return 0;
        }
        public int Stop()
        {
            Interlocked.Increment(ref Stops);
            return 0;
        }
    }
}
