using System;
using System.Collections.Generic;
using System.IO;
using NavalCommand.SeaPower.Client;

namespace NavalCommand.SeaPower.Mod;

internal sealed class IniAlternativeDisplayNameProvider : IAlternativeDisplayNameProvider
{
    readonly Dictionary<string, string> unitNames = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> equipmentNames = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> ammunitionNames = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> systemNames = new(StringComparer.OrdinalIgnoreCase);

    public IniAlternativeDisplayNameProvider(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An alternative display name path is required.", nameof(filePath));
        }
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Alternative display name file is missing.", filePath);
        }

        string section = "";
        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(filePath))
        {
            ++lineNumber;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2).Trim();
                if (!IsSupportedSection(section))
                {
                    throw InvalidLine(lineNumber, "unsupported section " + section);
                }
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw InvalidLine(lineNumber, "expected typeId=displayName");
            }

            string key = line.Substring(0, separatorIndex).Trim();
            string displayName = line.Substring(separatorIndex + 1).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw InvalidLine(lineNumber, "display names cannot be empty");
            }

            Dictionary<string, string> mappings = GetMappings(section, lineNumber);
            if (!IsValidKey(section, key))
            {
                throw InvalidLine(lineNumber, "invalid mapping key " + key);
            }
            if (!mappings.TryAdd(key, displayName))
            {
                throw InvalidLine(lineNumber, "duplicate mapping key " + key);
            }
        }

        if (unitNames.Count == 0 && equipmentNames.Count == 0 && ammunitionNames.Count == 0 && systemNames.Count == 0)
        {
            throw new InvalidDataException("Alternative display name file contains no mappings.");
        }
    }

    public bool TryGetUnitAlternativeDisplayName(string typeId, out string displayName)
    {
        if (unitNames.TryGetValue(typeId ?? "", out displayName))
        {
            return true;
        }

        const string ammunitionPrefix = "ammunition/";
        return typeId != null && typeId.StartsWith(ammunitionPrefix, StringComparison.OrdinalIgnoreCase)
            && ammunitionNames.TryGetValue(typeId.Substring(ammunitionPrefix.Length), out displayName);
    }

    public bool TryGetEquipmentAlternativeDisplayName(string typeId, out string displayName)
    {
        if (equipmentNames.TryGetValue(typeId ?? "", out displayName))
        {
            return true;
        }

        int separatorIndex = (typeId ?? "").LastIndexOf('/');
        if (separatorIndex < 0 || separatorIndex == typeId.Length - 1)
        {
            displayName = null;
            return false;
        }

        string nativeNameKey = typeId.Substring(separatorIndex + 1);
        return ammunitionNames.TryGetValue(nativeNameKey, out displayName)
            || systemNames.TryGetValue(nativeNameKey, out displayName);
    }

    Dictionary<string, string> GetMappings(string section, int lineNumber)
    {
        if (string.Equals(section, "UnitTypes", StringComparison.OrdinalIgnoreCase))
        {
            return unitNames;
        }
        if (string.Equals(section, "EquipmentTypes", StringComparison.OrdinalIgnoreCase))
        {
            return equipmentNames;
        }
        if (string.Equals(section, "Ammunition", StringComparison.OrdinalIgnoreCase))
        {
            return ammunitionNames;
        }
        if (string.Equals(section, "Systems", StringComparison.OrdinalIgnoreCase))
        {
            return systemNames;
        }

        throw InvalidLine(lineNumber, "mapping is outside a supported section");
    }

    static bool IsSupportedSection(string section)
    {
        return string.Equals(section, "UnitTypes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "EquipmentTypes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "Ammunition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(section, "Systems", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsValidKey(string section, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
        {
            return false;
        }
        if (string.Equals(section, "UnitTypes", StringComparison.OrdinalIgnoreCase))
        {
            return IsPath(key, 1);
        }
        if (string.Equals(section, "EquipmentTypes", StringComparison.OrdinalIgnoreCase))
        {
            return IsPath(key, 2);
        }
        return key.IndexOf('/') < 0;
    }

    static bool IsPath(string value, int minimumSeparators)
    {
        int separatorCount = 0;
        foreach (char character in value)
        {
            if (character == '/')
            {
                ++separatorCount;
            }
        }
        return separatorCount >= minimumSeparators
            && !value.StartsWith("/", StringComparison.Ordinal)
            && !value.EndsWith("/", StringComparison.Ordinal)
            && !value.Contains("..", StringComparison.Ordinal);
    }

    static InvalidDataException InvalidLine(int lineNumber, string reason)
    {
        return new InvalidDataException("Invalid alternative display name mapping at line " + lineNumber + ": " + reason + ".");
    }
}
