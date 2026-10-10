using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace NavalCommand.SeaPower.Harness;

// The writer holds only strings and scalar state, never a Unity object.
internal sealed class EvidenceWriter
{
    private const int MaximumMessages = 128;
    private readonly ConcurrentQueue<string> messages = new();
    private readonly string directory;
    private readonly string token;
    private readonly int processId;
    private volatile bool stop;
    private int dropped;
    public volatile bool Failed;
    public string Error;

    public EvidenceWriter(string outputDirectory, string runToken, int ownedProcessId)
    {
        directory = outputDirectory;
        token = runToken;
        processId = ownedProcessId;
        new Thread(Run) { IsBackground = true, Name = "SeaPower.Harness evidence" }.Start();
    }

    public void Emit(string message)
    {
        if (messages.Count >= MaximumMessages)
        {
            Interlocked.Increment(ref dropped);
            return;
        }
        messages.Enqueue(DateTime.UtcNow.ToString("o") + " " + message);
    }

    public void Stop() => stop = true;

    private void Run()
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var log = new StreamWriter(Path.Combine(directory, "harness.log"), false) { AutoFlush = true };
            while (!stop || !messages.IsEmpty)
            {
                if (Volatile.Read(ref dropped) != 0)
                {
                    throw new InvalidOperationException("evidence_queue_overflow");
                }
                while (messages.TryDequeue(out string message))
                {
                    log.WriteLine(message);
                    if (message.Contains(" playable-ready "))
                    {
                        WriteAtomic("ready.json", "{\"ready\":true,\"token\":\"" + token + "\",\"pid\":" + processId + "}");
                    }
                    if (message.Contains(" failed-"))
                    {
                        throw new InvalidOperationException(message);
                    }
                }
                Thread.Sleep(25);
            }
        }
        catch (Exception exception)
        {
            Error = exception.ToString();
            Failed = true;
            try
            {
                WriteAtomic("evidence-error.json", "{\"error\":\"writer-or-launch-failed\",\"type\":\"" + exception.GetType().Name + "\"}");
            }
            catch (Exception receiptException)
            {
                Error += "\nReceipt write failed: " + receiptException;
            }
        }
    }

    private void WriteAtomic(string name, string contents)
    {
        string destination = Path.Combine(directory, name);
        string temporary = destination + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, destination);
    }
}
