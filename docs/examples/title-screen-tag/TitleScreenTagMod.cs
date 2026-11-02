// TitleScreenTag - educational DCCM example (independent, no game code copied).
//
// Appends a suffix to the title-screen build label using an orig-first,
// fail-open hook. Demonstrates the minimal safe modding pattern only.

using HaxeProxy.Runtime;
using ModCore.Mods;
using Serilog;

namespace TitleScreenTag;

public class TitleScreenTagMod(ModInfo info) : ModBase(info)
{
    private const string Suffix = "TitleScreenTag - Dev Build";

    private bool initialized;

    public delegate void OrigSetMiscTexts(object self);

    public override void Initialize()
    {
        if (initialized) return;
        initialized = true;
        HashlinkHooks.Instance
            .CreateHook("pr.TitleScreen", "setMiscTexts", SetMiscTexts)
            .Enable();
        Logger.Information("TitleScreenTag: hook attached");
    }

    private void SetMiscTexts(OrigSetMiscTexts orig, object self)
    {
        orig(self);
        try
        {
            dynamic screen = self;
            if (screen.build == null) return;
            string before = screen.build.text?.ToString() ?? "";
            string display = string.IsNullOrWhiteSpace(before)
                ? Suffix
                : before + " | " + Suffix;
            screen.build.set_text(display.AsHaxeString());
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "TitleScreenTag: label append failed; leaving stock label");
        }
    }
}
