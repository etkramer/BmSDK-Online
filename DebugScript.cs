using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;

[Script]
public class DebugScript : Script
{
    public override void OnEnterMenu()
    {
        // Enable info display
        Game.GetGameViewportClient().bShowSessionDebug = true;
    }

    public override void OnKeyDown(Keys key)
    {
        // Debug actions based on key press.
        if (key == Keys.Enter)
        {
            Debug.Log("Entering game");
            DebugLoadGame();
        }
        else if (key == Keys.V)
        {
            Debug.Log("Toggling ghost");
            DebugToggleGhost();
        }
    }

    private static void DebugLoadGame()
    {
        var console = Game.GetConsole();
        console.ConsoleCommand(
            "start batentry?Players=Playable_Batman?Area=BaneSS,BaneSS_B1?Chapters=1"
        );
    }

    private static void DebugToggleGhost()
    {
        var cheatManager = Game.GetCheatManager();
        cheatManager.ToggleGhost();
    }
}
