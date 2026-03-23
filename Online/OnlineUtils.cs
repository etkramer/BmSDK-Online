using System.Net;
using BmSDK.Engine;

namespace Online;

public static class OnlineUtils
{
    public const double TickRate = 60;

    public static IPEndPoint GetLocalEndPoint()
    {
        return new IPEndPoint(IPAddress.Loopback, 8888);
    }

    public static T GetScriptComponent<T>(this Actor actor) where T : ScriptComponent
    {
        return (T)actor.ScriptComponents.FirstOrDefault(comp => comp is T);
    }
}
