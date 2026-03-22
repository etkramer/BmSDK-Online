namespace Online.Components;

/// <summary>
/// Base class for networked components. Provides NetId for identification.
/// See NetPlayerComponent for player-specific sync logic.
/// </summary>
public abstract class NetComponent : ScriptComponent
{
    public int NetId { get; protected set; } = -1;

    protected NetComponent()
    {
        NetId = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
    }

    public void SetNetId(int netId)
    {
        NetId = netId;
    }
}