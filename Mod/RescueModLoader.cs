using Game;

namespace WorldRescue;

public sealed class RescueModLoader : ModLoader {
    public override void __ModInitialize() => ModsManager.RegisterHook("OnSwitchScreen", this);

    public override void OnSwitchScreen(ref Screen screen, object[] parameters) {
        // Preserve the host's snapshot/upgrade flow. Intercept before its Loading state creates entities.
        if (screen.GetType() == typeof(GameLoadingScreen)) screen = new RescueLoadingScreen();
    }
}
