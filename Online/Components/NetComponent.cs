using Online.Server;

namespace Online;

public class NetComponent : ScriptComponent
{
    public int NetId { get; private set; } = -1;

    public NetComponent()
    {
        NetId = BitConverter.ToInt32(Guid.NewGuid().ToByteArray());
    }

    public override void OnAttach()
    {
        // TODO:
        // Server: send ActorSpawnMessage to all clients
        // Client: send ActorSpawnMessage to server (which forwards it to other clients)

        base.OnAttach();
    }

    public override void OnTick()
    {
        // TODO:
        // Server: send new actor position to all clients

        base.OnTick();
    }
}