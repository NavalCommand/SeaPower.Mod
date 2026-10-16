using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace NavalCommand.SeaPower.Mod;

[DataContract]
public sealed class RuntimeConfiguration
{
    [DataMember(Name = "schema", IsRequired = true)] public int Schema { get; set; }
    [DataMember(Name = "abi", IsRequired = true)] public int Abi { get; set; }
    [DataMember(Name = "enabledByDefault", IsRequired = true)] public bool Enabled { get; set; }
    [DataMember(Name = "endpoint", IsRequired = true)] public string Endpoint { get; set; }
    [DataMember(Name = "maxMessageBytes", IsRequired = true)] public int MaximumMessageBytes { get; set; }
    [DataMember(Name = "maxQueuedControls", IsRequired = true)] public int MaximumQueuedControls { get; set; }
    [DataMember(Name = "clientAutoLaunch", IsRequired = true)] public bool ClientAutoLaunch { get; set; }
    [DataMember(Name = "nativeLibrary")] public string NativeLibrary { get; set; }
    [DataMember(Name = "logDirectory")] public string LogDirectory { get; set; }
    [DataMember(Name = "classificationMode")] public string ClassificationMode { get; set; }
    [DataMember(Name = "platformRoleMappings")] public string PlatformRoleMappings { get; set; }
    [DataMember(Name = "alternativeDisplayNames")] public string AlternativeDisplayNames { get; set; }
    public string PlatformRoleMappingsPath { get; private set; }
    public string AlternativeDisplayNamesPath { get; private set; }

    public static RuntimeConfiguration Load(string moduleDirectory)
    {
        string configPath = Environment.GetEnvironmentVariable("NC_MOD_CONFIG") ?? Path.Combine(moduleDirectory, "config.json");
        if (!Path.IsPathRooted(configPath))
        {
            throw new ArgumentException("Configuration path must be absolute.");
        }
        using var stream = File.OpenRead(configPath);
        var configuration = (RuntimeConfiguration)new DataContractJsonSerializer(typeof(RuntimeConfiguration)).ReadObject(stream);
        if (configuration == null || configuration.Schema != 2 || configuration.Abi != 2)
        {
            throw new InvalidDataException("Unsupported configuration schema or ABI.");
        }
        configuration.ClassificationMode = (configuration.ClassificationMode ?? "safe").Trim().ToLowerInvariant();
        if (configuration.ClassificationMode != "safe" && configuration.ClassificationMode != "aggressive")
        {
            throw new ArgumentException("classificationMode must be safe or aggressive.");
        }
        string enabledOverride = Environment.GetEnvironmentVariable("NC_MOD_ENABLED");
        if (enabledOverride != null)
        {
            if (enabledOverride != "0" && enabledOverride != "1")
            {
                throw new ArgumentException("NC_MOD_ENABLED must be 0 or 1.");
            }
            configuration.Enabled = enabledOverride == "1";
        }
        var endpointMatch = Regex.Match(configuration.Endpoint ?? "", @"^127\.0\.0\.1:([0-9]+)$");
        if (!endpointMatch.Success || !int.TryParse(endpointMatch.Groups[1].Value, out int port) || port < 1024 || port > 65535)
        {
            throw new ArgumentException("A loopback endpoint with port 1024..65535 is required.");
        }
        // These existing ABI/provider bounds are fixed, not configurable capacities.
        if (configuration.MaximumMessageBytes != 16_777_216 || configuration.MaximumQueuedControls != 64 || configuration.ClientAutoLaunch)
        {
            throw new ArgumentException("Unsupported queue/message limits or automatic client launch.");
        }
        configuration.NativeLibrary = ResolvePath(moduleDirectory, configuration.NativeLibrary ?? "SeaPower.Host.dll");
        configuration.LogDirectory = ResolvePath(moduleDirectory, Environment.GetEnvironmentVariable("NC_MOD_LOG_DIRECTORY")
            ?? configuration.LogDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NavalCommand", "SeaPower.Mod"));
        configuration.PlatformRoleMappingsPath = ResolvePath(moduleDirectory, configuration.PlatformRoleMappings ?? "platform-roles.ini");
        configuration.AlternativeDisplayNamesPath = ResolvePath(moduleDirectory, configuration.AlternativeDisplayNames ?? "alternative-display-names.ru.ini");
        return configuration;
    }

    private static string ResolvePath(string moduleDirectory, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new ArgumentException("An empty runtime path is invalid.");
        }
        return Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(moduleDirectory, configuredPath));
    }
}
