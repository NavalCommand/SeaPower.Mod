namespace NavalCommand.SeaPower.Mod;

// Byte-only worker boundary; regression tests can exercise backpressure and shutdown.
internal interface INativeTransport
{
    int Start(string endpoint);
    int PublishView(byte[] bytes);
    int PublishResult(byte[] bytes);
    int PollControl(byte[] buffer, out uint size);
    int Stop();
}
