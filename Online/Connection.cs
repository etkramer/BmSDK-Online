using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Online;

public abstract class Connection
{
    public Socket Socket { get; private set; }
    public Queue<Message> MessageQueue { get; private set; } = new();

    private readonly byte[] messageBuffer = new byte[Message.BufferSize];

    public Connection(Socket socket)
    {
        Socket = socket;

        // Start listening for messages
        Socket.BeginReceive(messageBuffer, 0, messageBuffer.Length, SocketFlags.None, OnSocketRecieve, Socket);
    }

    public void ProcessMessages()
    {
        lock (MessageQueue)
        {
            while (MessageQueue.Count > 0)
            {
                var message = MessageQueue.Dequeue();
                ProcessMessage(message);
            }
        }
    }

    protected virtual void ProcessMessage(Message message) { }

    private void OnSocketRecieve(IAsyncResult result)
    {
        lock (MessageQueue)
        {
            // Recieve data from client
            Socket.EndReceive(result);
            Span<byte> data = messageBuffer.AsSpan();

            // Extract type id and json data (null terminated)
            var typeId = data[0];
            var json = Encoding.ASCII.GetString(data[1..]);
            json = json.TrimEnd('\0');

            // Deserialize message
            Message message = typeId switch
            {
                1 => JsonSerializer.Deserialize<JoinMessage>(json, Message.SerializerOptions),
                2 => JsonSerializer.Deserialize<ActorMoveMessage>(json, Message.SerializerOptions),
                3 => JsonSerializer.Deserialize<ActorSpawnMessage>(json, Message.SerializerOptions),
                _ => throw new NotSupportedException($"Encountered unknown message type {typeId}"),
            };

            // Add message to queue (to process in tick)
            MessageQueue.Enqueue(message);

            // Resume listening for messages
            Socket.BeginReceive(messageBuffer, 0, messageBuffer.Length, SocketFlags.None, OnSocketRecieve, Socket);
        }
    }
}