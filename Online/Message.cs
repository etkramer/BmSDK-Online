using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using BmSDK;

namespace Online;

public record class JoinMessage(string DisplayName, int NetId) : Message
{
    public override byte TypeId => 1;
}

public record class ActorMoveMessage(
    int NetId,
    Vector3 NewLocation,
    Rotator NewRotation,
    Vector3 MoveDirection,
    Rotator ControllerRotation
) : Message
{
    public override byte TypeId => 2;
}

public record class ActorSpawnMessage(int NetId, string ActorClass, Vector3 Location, Rotator Rotation) : Message
{
    public override byte TypeId => 3;
}

public record class PlayerInputMessage(
    int NetId,
    float AForward,
    float AStrafe,
    byte BCrouchButton,
    byte BRunButton,
    Vector3 InputHeading,
    byte BReadyGadgetButton,
    byte AGrappleButton,
    float ATurn,
    float ALookUp
) : Message
{
    public override byte TypeId => 4;
}

public record class InputEventMessage(int NetId, string EventName) : Message
{
    public override byte TypeId => 5;
}

public abstract record class Message
{
    public const short BufferSize = 512;

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        IncludeFields = true,
    };

    // NOTE: Remember to update Connection.OnSocketRecieve() when changing this
    public abstract byte TypeId { get; }

    public void Send(Socket socket)
    {
        // Construct buffer (first byte reserved for message type)
        Span<byte> buffer = new byte[BufferSize];
        buffer[0] = TypeId;

        // Store json in buffer
        var json = JsonSerializer.Serialize(this, GetType(), SerializerOptions);
        Encoding.ASCII.GetBytes(json, buffer[1..]);

        // Send message
        socket.Send(buffer);
    }
}
