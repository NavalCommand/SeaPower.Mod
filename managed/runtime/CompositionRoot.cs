using System;
using NavalCommand.SeaPower.Client;

namespace NavalCommand.SeaPower.Mod;

internal static class CompositionRoot
{
    public static Runtime Create(RuntimeConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var roleMappings = new IniPlatformRoleMappingProvider(configuration.PlatformRoleMappingsPath);
        var alternativeDisplayNames = new IniAlternativeDisplayNameProvider(configuration.AlternativeDisplayNamesPath);
        ClassificationMode mode = configuration.ClassificationMode == "aggressive"
            ? ClassificationMode.Aggressive : ClassificationMode.Safe;
        var game = new NativeGameFacade(roleMappings, alternativeDisplayNames, mode);
        return new Runtime(game);
    }
}
