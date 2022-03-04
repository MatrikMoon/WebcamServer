using System.Net.Sockets;

namespace Shared.Sockets
{
    public class ConnectedUser
    {
        public Guid id;
        public Socket socket = null;
        public NetworkStream networkStream = null;
        public const int BufferSize = 8192;
        public byte[] buffer = new byte[BufferSize];
        public List<byte> accumulatedBytes = new();
    }
}
