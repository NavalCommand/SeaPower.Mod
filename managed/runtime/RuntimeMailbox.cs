using System.Collections.Concurrent;

namespace NavalCommand.SeaPower.Mod;

// Worker receives only plain queues/state; it never closes over this runtime.
internal sealed class RuntimeMailbox
{
    public readonly ConcurrentQueue<byte[]> Views = new();
    public readonly ConcurrentQueue<byte[]> Results = new();
    public readonly ConcurrentQueue<byte[]> Commands = new();
    public readonly ConcurrentQueue<string> Events = new();
    public volatile bool Stop;
    public volatile bool Failed;
    public volatile bool Completed;
    public string Error;
}
