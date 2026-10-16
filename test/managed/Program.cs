using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Google.Protobuf;
using NavalCommand.Gateway.Wire;
using NavalCommand.SeaPower.Client;
using NavalCommand.SeaPower.Mod;

static void Check(bool passed, string description)
{
    if (!passed)
    {
        throw new InvalidOperationException(description);
    }
}

string directory = Path.Combine(Path.GetTempPath(), "NavalCommand.RuntimeTest", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
const string valid = "{\"schema\":2,\"abi\":2,\"enabledByDefault\":false,\"endpoint\":\"127.0.0.1:50051\",\"clientAutoLaunch\":false,\"maxMessageBytes\":16777216,\"maxQueuedControls\":64}";
File.WriteAllText(Path.Combine(directory, "config.json"), valid);
var configuration = RuntimeConfiguration.Load(directory);
configuration.LogDirectory = directory;
Check(configuration.Abi == 2 && !configuration.Enabled, "explicit version and disabled deployment default");
Check(configuration.ClassificationMode == "safe" && configuration.PlatformRoleMappingsPath == Path.Combine(directory, "platform-roles.ini"), "safe classification and mapping path defaults");
File.WriteAllText(Path.Combine(directory, "platform-roles.ini"), "[PlatformRoles]\nvessels/example=SurfaceWarfare,AntiSubmarineWarfare\nvessels/unarmed=\n[PrimaryPlatformRoles]\nvessels/example=AntiSubmarineWarfare\nvessels/unarmed=\n");
var mappings = new IniPlatformRoleMappingProvider(Path.Combine(directory, "platform-roles.ini"));
Check(mappings.TryGetRoles("vessels/example", out var mappedRoles)
    && mappedRoles.Count == 2 && mappedRoles.Contains(PlatformRole.SurfaceWarfare), "INI role map resolves typed role overrides");
Check(mappings.TryGetRoles("vessels/unarmed", out var emptyRoles) && emptyRoles.Count == 0, "empty INI mapping removes all platform roles");
Check(!mappings.TryGetRoles("vessels/unmapped", out _), "unmapped type uses native role defaults");
Check(mappings.TryGetPrimaryRole("vessels/example", out var primaryRole) && primaryRole == PlatformRole.AntiSubmarineWarfare,
    "primary role is configured independently of capability order");
Check(mappings.TryGetPrimaryRole("vessels/unarmed", out var clearedPrimary) && clearedPrimary == null,
    "empty primary entry explicitly clears the native primary role");
Check(!mappings.TryGetPrimaryRole("vessels/unmapped", out _), "unmapped primary role retains the native default");
string defaultMappingPath = Path.Combine(AppContext.BaseDirectory, "platform-roles.ini");
var defaults = new IniPlatformRoleMappingProvider(defaultMappingPath);
Check(defaults.TryGetRoles("vessels/wp_bpk_udaloy", out var udaloyCapabilities) && udaloyCapabilities.Contains(PlatformRole.AntiSubmarineWarfare),
    "Udaloy retains ASW capability");
Check(defaults.TryGetPrimaryRole("vessels/wp_bpk_udaloy", out var udaloyPrimary) && udaloyPrimary == PlatformRole.AntiSubmarineWarfare,
    "Udaloy retains principal ASW role");
foreach (string typeId in new[] { "vessels/wp_ss_kilo", "vessels/usn_cvn_nimitz", "aircraft/usn_fa-18a", "aircraft/usn_f-4j" })
{
    Check(defaults.TryGetPrimaryRole(typeId, out var primary) && !primary.HasValue, "multipurpose type explicitly clears native primary: " + typeId);
    Check(defaults.TryGetRoles(typeId, out var roles) && roles.Count > 0, "multipurpose platform retains capabilities: " + typeId);
}
var reviewedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "platform-primary-roles.csv")).Skip(1))
{
    // The first two columns contain identifiers/enums with no CSV quoting.
    string[] fields = line.Split(',');
    string typeId = fields[0];
    string expectedPrimary = fields[1];
    Check(reviewedIds.Add(typeId), "one reviewed decision per exact type");
    Check(defaults.TryGetRoles(typeId, out var capabilities), "reviewed type has capability entry: " + typeId);
    Check(defaults.TryGetPrimaryRole(typeId, out var reviewedPrimary), "reviewed type has explicit primary presence/absence: " + typeId);
    Check((reviewedPrimary?.ToString() ?? "") == expectedPrimary, "generated primary agrees with reviewed decision: " + typeId);
    Check(!reviewedPrimary.HasValue || capabilities.Contains(reviewedPrimary.Value), "present primary belongs to capabilities: " + typeId);
}
Check(reviewedIds.Count == 1002, "complete installed platform and ammunition review inventory");
foreach (string invalidPrimary in new[] { "SurfaceWarfare,AirDefence", "NotARole", "0" })
{
    File.WriteAllText(Path.Combine(directory, "platform-roles.ini"), "[PrimaryPlatformRoles]\nvessels/example=" + invalidPrimary + "\n");
    bool rejected = false;
    try
    {
        _ = new IniPlatformRoleMappingProvider(Path.Combine(directory, "platform-roles.ini"));
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Check(rejected, "invalid primary role is rejected");
}
File.WriteAllText(Path.Combine(directory, "platform-roles.ini"), "[PlatformRoles]\nvessels/example=NotARole\n");
bool invalidRoleRejected = false;
try
{
    _ = new IniPlatformRoleMappingProvider(Path.Combine(directory, "platform-roles.ini"));
}
catch (InvalidDataException)
{
    invalidRoleRejected = true;
}
Check(invalidRoleRejected, "invalid INI role is rejected");
File.WriteAllText(Path.Combine(directory, "platform-roles.ini"), "[PlatformRoles]\nvessels/example=0\n");
invalidRoleRejected = false;
try
{
    _ = new IniPlatformRoleMappingProvider(Path.Combine(directory, "platform-roles.ini"));
}
catch (InvalidDataException)
{
    invalidRoleRejected = true;
}
Check(invalidRoleRejected, "numeric INI roles are rejected");
File.WriteAllText(Path.Combine(directory, "platform-roles.ini"), "[PlatformRoles]\nvessels/example=SurfaceWarfare\n");
File.WriteAllText(Path.Combine(directory, "config.json"), valid.Replace("\"enabledByDefault\":false", "\"enabledByDefault\":false,\"classificationMode\":\"aggressive\",\"platformRoleMappings\":\"platform-roles.ini\""));
Check(RuntimeConfiguration.Load(directory).ClassificationMode == "aggressive", "aggressive classification mode is configurable");
foreach (string invalid in new[] { valid.Replace("\"abi\":2", "\"abi\":1"), valid.Replace("16777216", "1048576"), valid.Replace("127.0.0.1", "0.0.0.0") })
{
    File.WriteAllText(Path.Combine(directory, "config.json"), invalid);
    bool rejected = false;
    try
    {
        RuntimeConfiguration.Load(directory);
    }
    catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException)
    {
        rejected = true;
    }
    Check(rejected, "invalid transport configuration rejected");
}

var mailbox = new RuntimeMailbox();
var transport = new FakeTransport();
var first = new Publication { Session = "old", Revision = 1 }.ToByteArray();
var second = new Publication { Session = "new", Revision = 2 }.ToByteArray();
mailbox.Views.Enqueue(first);
mailbox.Views.Enqueue(second);
mailbox.Results.Enqueue(new ControlResult { Request = "completed", Result = new Result { Success = new Empty() } }.ToByteArray());
var worker = new Thread(() => TransportWorker.Run(mailbox, configuration, _ => transport));
worker.Start();
var deadline = DateTime.UtcNow.AddSeconds(3);
while (DateTime.UtcNow < deadline && (transport.View == null || transport.Result == null || mailbox.Commands.IsEmpty))
{
    Thread.Sleep(10);
}
Check(transport.View != null && Publication.Parser.ParseFrom(transport.View).Session == "new", "latest immutable publication survives busy retry");
Check(transport.Result != null && ControlResult.Parser.ParseFrom(transport.Result).Request == "completed", "engine result reaches native worker");
Check(mailbox.Commands.TryDequeue(out var command) && Control.Parser.ParseFrom(command).Pause, "byte-only control reaches engine queue");
mailbox.Stop = true;
Check(worker.Join(3000) && mailbox.Completed && !mailbox.Failed && transport.Stops == 1, "native shutdown is owned once by worker");
var failedMailbox = new RuntimeMailbox();
TransportWorker.Run(failedMailbox, configuration, _ => throw new InvalidOperationException("native_start_failure"));
Check(failedMailbox.Failed && failedMailbox.Completed && failedMailbox.Error.Contains("native_start_failure"), "worker startup failure visible to engine");
Console.WriteLine("Managed runtime: configuration, immutable byte queues, backpressure, controls and shutdown passed.");
return 0;

sealed class FakeTransport : INativeTransport
{
    int viewCalls;
    bool controlSent;
    public byte[] View;
    public byte[] Result;
    public int Stops;
    public int Start(string endpoint) => 0;
    public int PublishView(byte[] bytes)
    {
        if (++viewCalls == 1)
        {
            return 1;
        }
        View = (byte[])bytes.Clone();
        return 0;
    }
    public int PublishResult(byte[] bytes)
    {
        Result = (byte[])bytes.Clone();
        return 0;
    }
    public int PollControl(byte[] buffer, out uint size)
    {
        size = 0;
        if (controlSent)
        {
            return 1;
        }
        controlSent = true;
        var bytes = new Control { Client = "client", Session = "new", Request = "request", Pause = true }.ToByteArray();
        size = (uint)bytes.Length;
        Buffer.BlockCopy(bytes, 0, buffer, 0, bytes.Length);
        return 0;
    }
    public int Stop()
    {
        ++Stops;
        return 0;
    }
}
