using Shared.Models;
using Shared.Models.Packets;
using Shared.Sockets;
using Shared.Utilities;
using System.Net;

namespace Shared
{
    public class SystemServer
    {
        Server server;

        public event Func<User, Task> UserConnected;
        public event Func<User, Task> UserDisconnected;
        public event Func<User, Task> UserInfoUpdated;

        public event Func<Acknowledgement, Guid, Task> AckReceived;

        //Tournament State can be modified by ANY client thread, so definitely needs thread-safe accessing
        private State State {get;set;}

        public User Self { get; set; }

        //Server settings
        private Config config;
        private int port;
        private ServerSettings settings;

        public SystemServer()
        {
            config = new Config("serverConfig.json");

            var portValue = config.GetString("port");
            if (portValue == string.Empty)
            {
                portValue = "10356";
                config.SaveString("port", portValue);
            }

            var passwordValue = config.GetString("password");
            if (passwordValue == string.Empty || passwordValue == "[Password]")
            {
                passwordValue = string.Empty;
                config.SaveString("password", "[Password]");
            }

            settings = new ServerSettings
            {
                Password = passwordValue
            };

            port = int.Parse(portValue);
        }

        public void Start()
        {
            State = new State();
            State.ServerSettings = settings;

            //Give our new server a sense of self :P
            Self = new User()
            {
                Id = Guid.Empty.ToString(),
                Name = "HOST"
            };

            server = new Server(port, System.Net.Sockets.ProtocolType.Tcp);
            server.PacketReceived += Server_PacketReceived;
            server.ClientConnected += Server_ClientConnected;
            server.ClientDisconnected += Server_ClientDisconnected;

            Task.Run(server.Start);
        }

        private async Task Server_ClientDisconnected(ConnectedUser client)
        {
            Logger.Debug("Client Disconnected!");

            if (State.Users.Any(x => x.Id == client.id.ToString()))
            {
                var user = State.Users.First(x => x.Id == client.id.ToString());
                await RemoveUser(user);
            }
        }

        private Task Server_ClientConnected(ConnectedUser client)
        {
            return Task.CompletedTask;
        }

        public async Task Send(Guid id, Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            await server.Send(id, new PacketWrapper(packet));
        }

        public async Task Send(Guid[] ids, Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            await server.Send(ids, new PacketWrapper(packet));
        }

        public async Task ForwardTo(Guid[] ids, Guid from, Packet packet)
        {
            packet.From = from.ToString();
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            await server.Send(ids, new PacketWrapper(packet));
        }

        public async Task BroadcastToAllClients(Packet packet)
        {
            packet.From = Self.Id;
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            await server.Broadcast(new PacketWrapper(packet));
        }

        static string LogPacket(Packet packet)
        {
            string secondaryInfo = string.Empty;
            if (packet.packetCase == Packet.packetOneofCase.Command)
            {
                var command = packet.Command;
                secondaryInfo = command.CommandType.ToString();
            }

            if (packet.packetCase == Packet.packetOneofCase.Event)
            {
                var @event = packet.Event;

                secondaryInfo = @event.ChangedObjectCase.ToString();
                if (@event.ChangedObjectCase == Event.ChangedObjectOneofCase.user_updated_event)
                {
                    var user = @event.user_updated_event.User;
                    secondaryInfo =
                        $"{secondaryInfo} from ({user.Id} : {user.Name})";
                }
            }

            if (packet.packetCase == Packet.packetOneofCase.ForwardingPacket)
            {
                var forwardedpacketCase = packet.ForwardingPacket.Packet.packetCase;
                secondaryInfo = $"{forwardedpacketCase}";
            }
            
            if (packet.packetCase == Packet.packetOneofCase.Frame)
            {
                secondaryInfo = $"{packet.Frame.Data.Length}";
            }

            return $"({packet.packetCase}) ({secondaryInfo})";
        }

        #region EventManagement
        public async Task AddUser(User user)
        {
            lock (State)
            {
                State.Users.Add(user);
            }

            var @event = new Event
            {
                user_added_event = new Event.UserAddedEvent
                {
                    User = user
                }
            };
            await BroadcastToAllClients(new Packet
            {
                Event = @event
            });

            if (UserConnected != null) await UserConnected.Invoke(user);
        }

        public async Task UpdateUser(User user)
        {
            lock (State)
            {
                var userToReplace = State.Users.FirstOrDefault(x => x.UserEquals(user));
                State.Users.Remove(userToReplace);
                State.Users.Add(user);
            }

            var @event = new Event
            {
                user_updated_event = new Event.UserUpdatedEvent
                {
                    User = user
                }
            };
            await BroadcastToAllClients(new Packet
            {
                Event = @event
            });

            if (UserInfoUpdated != null) await UserInfoUpdated.Invoke(user);
        }

        public async Task RemoveUser(User user)
        {
            lock (State)
            {
                var userToRemove = State.Users.FirstOrDefault(x => x.UserEquals(user));
                State.Users.Remove(userToRemove);
            }

            var @event = new Event
            {
                user_left_event = new Event.UserLeftEvent
                {
                    User = user
                }
            };
            await BroadcastToAllClients(new Packet
            {
                Event = @event
            });

            if (UserDisconnected != null) await UserDisconnected.Invoke(user);
        }
        #endregion EventManagement

        private async Task Server_PacketReceived(ConnectedUser user, PacketWrapper packet)
        {
            Logger.Debug($"Received data: {LogPacket(packet.Payload)}");

            //Ready to go, only disabled since it is currently unusued
            /*if (packet.Type != PacketType.Acknowledgement)
            {
                Send(packet.From, new Packet(new Acknowledgement()
                {
                    PacketId = packet.Id
                }));
            }*/

            if (packet.Payload.packetCase == Packet.packetOneofCase.Acknowledgement)
            {
                Acknowledgement acknowledgement = packet.Payload.Acknowledgement;
                AckReceived?.Invoke(acknowledgement, Guid.Parse(packet.Payload.From));
            }
            else if (packet.Payload.packetCase == Packet.packetOneofCase.Connect)
            {
                Connect connect = packet.Payload.Connect;

                if (connect.ClientVersion != SharedConstructs.VersionCode)
                {
                    await Send(user.id, new Packet
                    {
                        ConnectResponse = new ConnectResponse()
                        {
                            Response = new Response()
                            {
                                Type = Response.ResponseType.Fail,
                                Message = $"Version mismatch, this server is on version {SharedConstructs.Version}",
                            },
                            Self = null,
                            State = null,
                            ServerVersion = SharedConstructs.VersionCode
                        }
                    });
                }
                else if (connect.ClientType == Connect.ConnectTypes.User)
                {
                    var newUser = new User()
                    {
                        Id = user.id.ToString(),
                        Name = connect.Name
                    };

                    await AddUser(newUser);

                    //Give the newly connected user their Self and State
                    await Send(user.id, new Packet
                    {
                        ConnectResponse = new ConnectResponse()
                        {
                            Response = new Response()
                            {
                                Type = Response.ResponseType.Success,
                                Message = $"Connected to server!"
                            },
                            Self = newUser,
                            State = State,
                            ServerVersion = SharedConstructs.VersionCode
                        }
                    });
                }
                else if (connect.ClientType == Connect.ConnectTypes.TemporaryConnection)
                {
                    //A scraper just wants a copy of our state, so let's give it to them
                    await Send(user.id, new Packet
                    {
                        ConnectResponse = new ConnectResponse()
                        {
                            Response = new Response()
                            {
                                Type = Response.ResponseType.Success,
                                Message = $"Connected to server (scraper)!"
                            },
                            Self = null,
                            State = State,
                            ServerVersion = SharedConstructs.VersionCode
                        }
                    });
                }
            }
            else if (packet.Payload.packetCase == Packet.packetOneofCase.Event)
            {
                Event @event = packet.Payload.Event;
                switch (@event.ChangedObjectCase)
                {
                    case Event.ChangedObjectOneofCase.user_added_event:
                        await AddUser(@event.user_added_event.User);
                        break;
                    case Event.ChangedObjectOneofCase.user_updated_event:
                        await UpdateUser(@event.user_updated_event.User);
                        break;
                    case Event.ChangedObjectOneofCase.user_left_event:
                        await RemoveUser(@event.user_left_event.User);
                        break;
                    default:
                        Logger.Error($"Unknown command received from {user.id}!");
                        break;
                }
            }
            else if (packet.Payload.packetCase == Packet.packetOneofCase.ForwardingPacket)
            {
                var forwardingPacket = packet.Payload.ForwardingPacket;
                var forwardedPacket = forwardingPacket.Packet;

                await ForwardTo(forwardingPacket.ForwardToes.Select(x => Guid.Parse(x)).ToArray(),
                    Guid.Parse(packet.Payload.From),
                    forwardedPacket);
            }
        }
    }
}