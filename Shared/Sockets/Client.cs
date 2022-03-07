using System.Net;
using System.Net.Sockets;

namespace Shared.Sockets
{
    public class Client
    {
        public event Func<PacketWrapper, Task> PacketReceived;
        public event Func<PacketWrapper, Task> UDPPacketReceived;
        public event Func<Task> ServerConnected;
        public event Func<Task> ServerFailedToConnect;
        public event Func<Task> ServerDisconnected;

        private int serverPort;
        private string serverAddress;
        private int udpServerPort;
        private ConnectedUser player = new ConnectedUser();
        private UdpClient udpClient = new UdpClient();

        public bool Connected
        {
            get
            {
                return player?.socket?.Connected ?? false;
            }
        }

        public Client(string endpoint, int port, int udpPort)
        {
            serverAddress = endpoint;
            serverPort = port;
            udpServerPort = udpPort;
        }

        public async Task Start()
        {
            if (!IPAddress.TryParse(serverAddress, out var ipAddress))
            {
                //If we want to default to ipv4, we should uncomment the following line. I'm leaving it
                //as it is now so we can test ipv6/ipv4 mix stability
                //IPAddress ipAddress = ipHostInfo.AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                IPHostEntry ipHostInfo = Dns.GetHostEntry(serverAddress);
                ipAddress = ipHostInfo.AddressList[0];
            }

            IPEndPoint remoteEndpoint = new IPEndPoint(ipAddress, serverPort);
            IPEndPoint remoteUDPEndpoint = new IPEndPoint(ipAddress, udpServerPort);

            player.socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                //Try to connect to the server
                await player.socket.ConnectAsync(remoteEndpoint);
                var client = player.socket;

                //Try to authenticate with SSL
                player.networkStream = new NetworkStream(client, ownsSocket: true);

                //Signal Connection complete
                if (ServerConnected != null) await ServerConnected.Invoke();

                //Initiate UDP connection
                udpClient.Connect(remoteUDPEndpoint);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());

                if (ServerFailedToConnect != null) await ServerFailedToConnect.Invoke();
            }

            ReceiveLoop();
            UDPReceiveLoop();
        }

        private async void ReceiveLoop()
        {
            try
            {
                // Begin receiving the data from the remote device.  
                while (player?.socket?.Connected ?? false)
                {
                    var bytesRead = await player.networkStream!.ReadAsync(player.buffer, 0, ConnectedUser.BufferSize);
                    if (bytesRead > 0)
                    {
                        var currentBytes = new byte[bytesRead];
                        Buffer.BlockCopy(player.buffer, 0, currentBytes, 0, bytesRead);

                        player.accumulatedBytes.AddRange(currentBytes);
                        if (player.accumulatedBytes.Count >= PacketWrapper.packetHeaderSize)
                        {
                            //If we're not at the start of a packet, increment our position until we are, or we run out of bytes
                            var accumulatedBytes = player.accumulatedBytes.ToArray();
                            while (accumulatedBytes.Length >= PacketWrapper.packetHeaderSize && !PacketWrapper.StreamIsAtPacket(accumulatedBytes))
                            {
                                player.accumulatedBytes.RemoveAt(0);
                                accumulatedBytes = player.accumulatedBytes.ToArray();
                            }

                            while (accumulatedBytes.Length >= PacketWrapper.packetHeaderSize && PacketWrapper.PotentiallyValidPacket(accumulatedBytes))
                            {
                                PacketWrapper readPacket = null;
                                try
                                {
                                    readPacket = PacketWrapper.FromBytes(accumulatedBytes);
                                    if (PacketReceived != null) await PacketReceived.Invoke(readPacket);
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e.Message);
                                    Logger.Error(e.StackTrace!);
                                }

                                //Remove the bytes which we've already used from the accumulated List
                                //If the packet failed to parse, skip the header so that the rest of the packet is consumed by the above vailidity check on the next run
                                player.accumulatedBytes.RemoveRange(0, readPacket?.Size ?? PacketWrapper.packetHeaderSize);
                                accumulatedBytes = player.accumulatedBytes.ToArray();
                            }
                        }
                    }
                    else if (bytesRead == 0) throw new Exception("Stream ended");
                }
            }
            catch (ObjectDisposedException)
            {
                await ServerDisconnected_Internal();
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                await ServerDisconnected_Internal();
            }
        }

        public async void UDPReceiveLoop()
        {
            while (true)
            {
                var result = await udpClient.ReceiveAsync();
                Logger.Debug($"UDP RECIEVE: {result.Buffer}");
                if (result.Buffer.Length > 0)
                {
                    //If we're not at the start of a packet, increment our position until we are, or we run out of bytes
                    if (PacketWrapper.PotentiallyValidPacket(result.Buffer))
                    {
                        try
                        {
                            var readPacket = PacketWrapper.FromBytes(result.Buffer);
                            if (UDPPacketReceived != null) await UDPPacketReceived.Invoke(readPacket);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e.Message);
                            Logger.Error(e.StackTrace!);
                        }
                    }
                }
            }
        }

        public async Task Send(PacketWrapper packet)
        {
            var data = packet.ToBytes();
            try
            {
                await player.networkStream!.WriteAsync(data, 0, data.Length);
            }
            catch
            {
                await ServerDisconnected_Internal();

                throw; //Ancestor functions will handle this and likely reset the connection
            }
        }

        public async Task SendUDP(PacketWrapper packet)
        {
            var data = packet.ToBytes();
            await udpClient.SendAsync(data);
        }

        /// <summary>
        /// Waits for a response to the request with the designated ID, or if none is supplied, 
        /// any request at all. After which, it will unsubscribe from the event
        /// </summary>
        /// <param name="requestPacket">The packet to send</param>
        /// <param name="onRecieved">A Function executed when a matching Packet is received. If no <paramref name="id"/> is provided, this will trigger on any Packet with an id.
        /// This Function should return a boolean indicating whether or not the request was satisfied. For example, if it returns True, the event subscription is cancelled and the timer
        /// destroyed, and no more messages will be parsed through the Function. If it returns false, it is assumed that the Packet was unsatisfactory, and the Function will continue to receive
        /// potential matches.</param>
        /// <param name="id">The id of the Packet to wait for. Optional. If none is provided, all Packets with ids will be sent to <paramref name="onRecieved"/>.</param>
        /// <param name="onTimeout">A Function that executes in the event of a timeout. Optional.</param>
        /// <param name="timeout">Duration in milliseconds before the wait times out.</param>
        /// <returns></returns>
        public async Task SendAndAwaitResponse(PacketWrapper requestPacket, Func<PacketWrapper, Task<bool>> onRecieved, string id = null, Func<Task> onTimeout = null, int timeout = 5000)
        {
            Func<PacketWrapper, Task> receivedPacket = null;

            //TODO: I don't think Register awaits async callbacks 
            var cancellationTokenSource = new CancellationTokenSource();
            var registration = cancellationTokenSource.Token.Register(async () =>
            {
                PacketReceived -= receivedPacket;
                if (onTimeout != null) await onTimeout.Invoke();

                cancellationTokenSource.Dispose();
            });

            receivedPacket = async (responsePacket) =>
            {
                if (id == null || responsePacket.Payload.Id.ToString() == id)
                {
                    if (await onRecieved(responsePacket))
                    {
                        PacketReceived -= receivedPacket;

                        registration.Dispose();
                        cancellationTokenSource.Dispose();
                    };
                }
            };

            cancellationTokenSource.CancelAfter(timeout);
            PacketReceived += receivedPacket;

            await Send(requestPacket);
        }

        private async Task ServerDisconnected_Internal()
        {
            Shutdown();
            if (ServerDisconnected != null) await ServerDisconnected.Invoke();
        }

        public void Shutdown()
        {
            player.networkStream?.Dispose();
        }
    }
}