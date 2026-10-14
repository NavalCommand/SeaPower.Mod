using System;
using System.Collections.Generic;
using System.IO;
using NavalCommand.SeaPower.Client;

namespace NavalCommand.SeaPower.Mod;

internal sealed class IniPlatformRoleMappingProvider : IPlatformRoleMappingProvider
{
    readonly Dictionary<string, IReadOnlyCollection<PlatformRole>> roleMappings = new(StringComparer.OrdinalIgnoreCase);

    public IniPlatformRoleMappingProvider(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A platform role mapping path is required.", nameof(filePath));
        }
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Platform role mapping file is missing.", filePath);
        }

        string section = "";
        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(filePath))
        {
            ++lineNumber;
            string line = rawLine.Trim();
            int semicolonComment = line.IndexOf(';');
            int hashComment = line.IndexOf('#');
            int commentStart = semicolonComment < 0 ? hashComment : hashComment < 0 ? semicolonComment : Math.Min(semicolonComment, hashComment);
            if (commentStart >= 0)
            {
                line = line.Substring(0, commentStart).Trim();
            }
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2).Trim();
                if (!string.Equals(section, "PlatformRoles", StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidLine(lineNumber, "unsupported section " + section);
                }
                continue;
            }
            if (!string.Equals(section, "PlatformRoles", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidLine(lineNumber, "mapping is outside [PlatformRoles]");
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw InvalidLine(lineNumber, "expected typeId=Role[,Role]");
            }
            string typeId = line.Substring(0, separatorIndex).Trim();
            if (!IsTypeId(typeId))
            {
                throw InvalidLine(lineNumber, "type IDs must use folder/name form");
            }

            var roles = new List<PlatformRole>();
            string roleList = line.Substring(separatorIndex + 1).Trim();
            if (roleList.Length > 0)
            {
                foreach (string value in roleList.Split(','))
                {
                    string name = value.Trim();
                    if (int.TryParse(name, out _)
                        || !Enum.TryParse(name, true, out PlatformRole role)
                        || !Enum.IsDefined(typeof(PlatformRole), role))
                    {
                        throw InvalidLine(lineNumber, "unknown platform role " + name);
                    }
                    if (roles.Contains(role))
                    {
                        throw InvalidLine(lineNumber, "duplicate platform role " + name);
                    }
                    roles.Add(role);
                }
            }
            if (!roleMappings.TryAdd(typeId, roles.AsReadOnly()))
            {
                throw InvalidLine(lineNumber, "duplicate type ID " + typeId);
            }
        }
        if (roleMappings.Count == 0)
        {
            throw new InvalidDataException("Platform role mapping file contains no [PlatformRoles] entries.");
        }
    }

    public bool TryGetRoles(string typeId, out IReadOnlyCollection<PlatformRole> roles)
    {
        return roleMappings.TryGetValue(typeId ?? "", out roles);
    }

    static bool IsTypeId(string typeId)
    {
        int separatorIndex = typeId.IndexOf('/');
        return separatorIndex > 0 && separatorIndex < typeId.Length - 1
            && typeId.IndexOf('/', separatorIndex + 1) < 0
            && !typeId.Contains("..", StringComparison.Ordinal)
            && string.Equals(typeId, typeId.Trim(), StringComparison.Ordinal);
    }

    static InvalidDataException InvalidLine(int lineNumber, string reason)
    {
        return new InvalidDataException("Invalid platform role mapping at line " + lineNumber + ": " + reason + ".");
    }
}
