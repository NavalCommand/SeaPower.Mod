using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using BepInEx;
using SeaPower;

namespace NavalCommand.SeaPower.LiveFixture;

// Separately installed, opt-in regression fixture. No production reference.
// I/O worker owns only strings; native input executes on Unity's main thread.
[BepInPlugin("dev.navalcommand.seapower.livefixture", "NavalCommand Native Player Regression", "0.1.0")]
public sealed class PlayerControls : BaseUnityPlugin
{
    private const int MaximumSequence = 100;
    private const int WorkerPollMilliseconds = 10;
    private readonly ConcurrentQueue<string> requests = new();
    private readonly ConcurrentQueue<string[]> responses = new();
    private volatile bool stop;

    private void Awake()
    {
        if (System.Environment.GetEnvironmentVariable("NC_FIXTURE_ENABLED") != "1")
        {
            return;
        }
        string directory = System.Environment.GetEnvironmentVariable("NC_HARNESS_RUN_DIRECTORY");
        if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
        {
            return;
        }
        new Thread(() => Run(directory)) { IsBackground = true, Name = "Native player fixture I/O" }.Start();
    }

    private void Run(string directory)
    {
        try
        {
            string previous = "";
            while (!stop || !responses.IsEmpty)
            {
                while (responses.TryDequeue(out var response))
                {
                    string destination = Path.Combine(directory, "fixture-response-" + response[0] + ".txt");
                    File.WriteAllText(destination + ".tmp", response[1]);
                    File.Move(destination + ".tmp", destination);
                }
                string request = Path.Combine(directory, "fixture-request.txt");
                if (!stop && requests.IsEmpty && File.Exists(request))
                {
                    string value = File.ReadAllText(request);
                    if (value != previous)
                    {
                        previous = value;
                        requests.Enqueue(value);
                    }
                }
                Thread.Sleep(WorkerPollMilliseconds);
            }
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(Path.Combine(directory, "fixture-error.txt"), exception.ToString());
            }
            catch (Exception)
            {
                // The output directory may be unavailable during teardown.
            }
            stop = true;
        }
    }

    private void Update()
    {
        if (stop || Globals.mainThread != Thread.CurrentThread || !Singleton<GameMain>.InstanceExists()
            || !Singleton<GameMain>.Instance._initialized || Singleton<GameMain>.Instance._closing)
        {
            return;
        }
        if (!requests.TryDequeue(out string request))
        {
            return;
        }
        string[] parts = request.Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int sequence) || sequence < 1 || sequence > MaximumSequence)
        {
            return;
        }
        try
        {
            switch (parts[1])
            {
                case "pause":
                    GameTime.Pause();
                    break;
                case "unpause":
                    GameTime.UnPause();
                    break;
                case "compression2":
                    GameTime.SetTimeCompression(2);
                    break;
                case "read":
                    break;
                default:
                    throw new ArgumentException("Unknown native fixture action");
            }
            responses.Enqueue(new[] { parts[0], (GameTime.IsPaused() ? "paused" : "running") + " "
                + GameTime.TimeCompression.ToString("R", CultureInfo.InvariantCulture) + " "
                + GameTime.missionElapsedTime.ToString("R", CultureInfo.InvariantCulture) });
        }
        catch (Exception exception)
        {
            responses.Enqueue(new[] { parts[0], "error " + exception });
        }
    }

    private void OnDestroy() => stop = true;
}
