﻿using System.Net;
using System.Net.Sockets;

namespace Shared.Sockets
{
    public class Server
    {
        public event Func<ConnectedUser, PacketWrapper, Task> PacketReceived;
        public event Func<IPEndPoint, PacketWrapper, Task> UDPPacketReceived;
        public event Func<ConnectedUser, Task> ClientConnected;
        public event Func<ConnectedUser, Task> ClientDisconnected;

        public bool Enabled { get; set; } = true;

        private List<ConnectedUser> clients = new List<ConnectedUser>();
        private Socket ipv4Server;
        private Socket ipv6Server;
        private UdpClient udpServer;
        private int port;
        private int udpPort;

        //Blocks while accepting new connections (forever, or until shutdown)
        public Task Start()
        {
            var ipv4Address = IPAddress.Any;
            var ipv6Address = IPAddress.IPv6Any;
            var localIPV4EndPoint = new IPEndPoint(ipv4Address, port);
            var localIPV6EndPoint = new IPEndPoint(ipv6Address, port);
            var udpIPV4EndPoint = new IPEndPoint(IPAddress.Any, udpPort);

            ipv4Server = new Socket(ipv4Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            ipv6Server = new Socket(ipv6Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            udpServer = new UdpClient(udpPort);

            ipv4Server.Bind(localIPV4EndPoint);
            ipv6Server.Bind(localIPV6EndPoint);

            ipv4Server.Listen(100);
            ipv6Server.Listen(100);

            Func<Socket, Task> processClient = async (clientSocket) =>
            {
                var ConnectedUser = new ConnectedUser
                {
                    socket = clientSocket,
                    id = Guid.NewGuid(),
                    networkStream = new NetworkStream(clientSocket, ownsSocket: true)
                };

                lock (clients)
                {
                    clients.Add(ConnectedUser);
                }

                if (ClientConnected != null) await ClientConnected.Invoke(ConnectedUser);

                ReceiveLoop(ConnectedUser);
            };

            Func<Task> ipv4Accept = async () =>
            {
                while (Enabled)
                {
                    // Start an asynchronous socket to listen for connections.  
                    Logger.Debug($"Waiting for an IPV4 connection on {ipv4Address}:{port} ...");
                    var clientSocket = await ipv4Server.AcceptAsync();
                    Logger.Debug($"Accepted connection on {ipv4Address}:{port} ...");

                    await processClient(clientSocket);
                }
            };

            Func<Task> ipv6Accept = async () =>
            {
                while (Enabled)
                {
                    // Start an asynchronous socket to listen for connections.  
                    Logger.Debug($"Waiting for an IPV6 connection on {ipv6Address}:{port} ...");
                    var clientSocket = await ipv6Server.AcceptAsync();
                    Logger.Debug($"Accpeted connection on {ipv6Address}:{port} ...");

                    await processClient(clientSocket);
                }
            };

            Task.Run(ipv4Accept);
            Task.Run(ipv6Accept);
            Task.Run(UdpReceiveLoop);
            return Task.CompletedTask;
        }

        public Server(int port, int udpPort)
        {
            this.port = port;
            this.udpPort = udpPort;
        }

        private async Task UdpReceiveLoop()
        {
            while (true)
            {
                var result = await udpServer.ReceiveAsync();
                Logger.Debug($"UDP RECIEVE: {result.Buffer}");
                if (result.Buffer.Length > 0)
                {
                    //If we're not at the start of a packet, increment our position until we are, or we run out of bytes
                    if (PacketWrapper.PotentiallyValidPacket(result.Buffer))
                    {
                        try
                        {
                            var readPacket = PacketWrapper.FromBytes(result.Buffer);
                            if (UDPPacketReceived != null) await UDPPacketReceived.Invoke(result.RemoteEndPoint, readPacket);
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

        private async void ReceiveLoop(ConnectedUser player)
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
                            while (accumulatedBytes.Length >= PacketWrapper.packetHeaderSize &&
                                   !PacketWrapper.StreamIsAtPacket(accumulatedBytes))
                            {
                                player.accumulatedBytes.RemoveAt(0);
                                accumulatedBytes = player.accumulatedBytes.ToArray();
                            }

                            while (accumulatedBytes.Length >= PacketWrapper.packetHeaderSize &&
                                   PacketWrapper.PotentiallyValidPacket(accumulatedBytes))
                            {
                                PacketWrapper readPacket = null;
                                try
                                {
                                    readPacket = PacketWrapper.FromBytes(accumulatedBytes);
                                    if (PacketReceived != null) await PacketReceived.Invoke(player, readPacket);
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
                await ClientDisconnected_Internal(player);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                await ClientDisconnected_Internal(player);
            }
        }

        private async Task ClientDisconnected_Internal(ConnectedUser player)
        {
            lock (clients)
            {
                clients.Remove(player);
            }

            if (ClientDisconnected != null) await ClientDisconnected.Invoke(player);
        }

        public async Task Broadcast(PacketWrapper packet)
        {
            try
            {
                var clientList = new List<ConnectedUser>();
                lock (clients)
                {
                    //We don't necessarily need to await this
                    foreach (var ConnectedUser in clients) clientList.Add(ConnectedUser);
                }

                var data = packet.ToBytes();
                await Task.WhenAll(clientList.Select(x => Send(x, data)).ToArray());
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
            }
        }

        public async Task Send(Guid id, PacketWrapper packet) => await Send(new Guid[] {id}, packet);

        public async Task Send(Guid[] ids, PacketWrapper packet)
        {
            try
            {
                var clientList = new List<ConnectedUser>();
                lock (clients)
                {
                    //We don't necessarily need to await this
                    foreach (var ConnectedUser in clients.Where(x => ids.Contains(x.id))) clientList.Add(ConnectedUser);
                }

                var data = packet.ToBytes();
                await Task.WhenAll(clientList.Select(x => Send(x, data)).ToArray());
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
            }
        }

        private async Task Send(ConnectedUser ConnectedUser, byte[] data)
        {
            try
            {
                await ConnectedUser.networkStream!.WriteAsync(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                await ClientDisconnected_Internal(ConnectedUser);
            }
        }

        /// <summary>
        /// Waits for a response to the request with the designated ID, or if none is supplied, 
        /// any request at all. After which, it will unsubscribe from the event
        /// </summary>
        /// <param name="clientId">The id of the client to which to send the request</param>
        /// <param name="requestPacket">The packet to send</param>
        /// <param name="onRecieved">A Function executed when a matching Packet is received. If no <paramref name="id"/> is provided, this will trigger on any Packet with an id.
        /// This Function should return a boolean indicating whether or not the request was satisfied. For example, if it returns True, the event subscription is cancelled and the timer
        /// destroyed, and no more messages will be parsed through the Function. If it returns false, it is assumed that the Packet was unsatisfactory, and the Function will continue to receive
        /// potential matches.</param>
        /// <param name="id">The id of the Packet to wait for. Optional. If none is provided, all Packets with ids will be sent to <paramref name="onRecieved"/>.</param>
        /// <param name="onTimeout">A Function that executes in the event of a timeout. Optional.</param>
        /// <param name="timeout">Duration in milliseconds before the wait times out.</param>
        /// <returns></returns>
        public async Task SendAndAwaitResponse(Guid clientId, PacketWrapper requestPacket, Func<PacketWrapper, Task<bool>> onRecieved, string id = null, Func<Task> onTimeout = null, int timeout = 5000)
        {
            Func<ConnectedUser, PacketWrapper, Task> receivedPacket = null;

            //TODO: I don't think Register awaits async callbacks 
            var cancellationTokenSource = new CancellationTokenSource();
            var registration = cancellationTokenSource.Token.Register(async () =>
            {
                PacketReceived -= receivedPacket;
                if (onTimeout != null) await onTimeout.Invoke();

                cancellationTokenSource.Dispose();
            });

            receivedPacket = async (client, responsePacket) =>
            {
                if (clientId == client.id && (id == null || responsePacket.Payload.Id.ToString() == id))
                {
                    if (await onRecieved(responsePacket))
                    {
                        PacketReceived -= receivedPacket;

                        registration.Dispose();
                        cancellationTokenSource.Dispose();
                    }

                    ;
                }
            };

            cancellationTokenSource.CancelAfter(timeout);
            PacketReceived += receivedPacket;

            await Send(clientId, requestPacket);
        }

        public async Task SendUDP(PacketWrapper packet, IPEndPoint endpoint) => await SendUDP(packet.ToBytes(), endpoint);

        public async Task SendUDP(byte[] data, IPEndPoint endpoint)
        {
            await udpServer.SendAsync(data, endpoint);
        }

        public void Shutdown()
        {
            Enabled = false;
            if (ipv4Server!.Connected) ipv4Server.Shutdown(SocketShutdown.Both);
            ipv4Server.Close();

            if (ipv6Server!.Connected) ipv6Server.Shutdown(SocketShutdown.Both);
            ipv6Server.Close();
        }
    }
}