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

        // console.ConsoleCommand(
        //     "start batentry?Players=Playable_Batman?Area=Church,Church_A1?Chapters=1"
        // );

        // console.ConsoleCommand(
        //     "start batentry?Players=Playable_Batman?"
        //         + "Area=OW,OW_A9,OW_A6,OW_A7,OW_A8,OW_R1,OW_R2,OW_E3,OW_E4,OW_A1_Static_LOD,OW_A2,OW_A3_Static_LOD,OW_A4_Static_LOD,OW_A5_Static_LOD,OW_R3,OW_E2_Static_LOD,OW_E6_Static_LOD,OW_E5_Static_LOD,OW_RE1_Static_LOD?"
        //         + "Flags=Vertical_Slice,Demo_Riddler_Door_Switch,Map_TriggeredCityStories,Batman_ResonatorCodes,Teleport_Church_To_Museum,Demo_Ryder_Bully,Demo_Courthouse_Lock,Public_Demo?"
        //         + "Chapters=1,1b,2,2a,Z1,V1?"
        //         + "Start=BeginVS?"
        // );
    }

    private static void DebugToggleGhost()
    {
        var cheatManager = Game.GetCheatManager();
        cheatManager.ToggleGhost();
    }
}
