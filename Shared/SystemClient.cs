using Shared.Models;
using Shared.Models.Packets;
using Shared.Sockets;
using Shared.Utilities;
using System.Timers;
using static Shared.Models.Packets.Connect;
using Timer = System.Timers.Timer;

namespace Shared
{
    public class SystemClient
    {
        public event Func<User, Task> UserConnected;
        public event Func<User, Task> UserDisconnected;
        public event Func<User, Task> UserInfoUpdated;
        
        public event Func<Acknowledgement, Guid, Task> AckReceived;
        public event Func<Frame, Task> FrameReceived;

        public event Func<ConnectResponse, Task> ConnectedToServer;
        public event Func<ConnectResponse, Task> FailedToConnectToServer;
        public event Func<Task> ServerDisconnected;

        public User Self { get; set; }

        public State State { get; set; }

        protected Client Client { get; set; }

        public bool Connected => Client?.Connected ?? false;

        private Timer heartbeatTimer = new();
        private bool shouldHeartbeat;
        private string endpoint;
        private int port;
        private string username;
        private string password;
        private string userId;
        private ConnectTypes connectType;

        public SystemClient(string endpoint, int port, string username, ConnectTypes connectType, string userId = "0", string password = null)
        {
            this.endpoint = endpoint;
            this.port = port;
            this.username = username;
            this.password = password;
            this.userId = userId;
            this.connectType = connectType;
        }

        //Blocks until connected (or failed), then returns
        public async Task Start()
        {
            shouldHeartbeat = true;
            heartbeatTimer.Interval = 10000;
            heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;

            await ConnectToServer();
        }

        private void HeartbeatTimer_Elapsed(object _, ElapsedEventArgs __)
        {
            //Send needs to be awaited so it will properly catch exceptions, but we can't make this timer callback async. So, we do this.
            Task.Run(async () =>
            {
                try
                {
                    var command = new Command
                    {
                        CommandType = Command.CommandTypes.Heartbeat
                    };
                    await Send(new Packet
                    {
                        Command = command
                    });
                }
                catch (Exception e)
                {
                    Logger.Debug("HEARTBEAT FAILED");
                    Logger.Debug(e.ToString());

                    await ConnectToServer();
                }
            });
        }

        private async Task ConnectToServer()
        {
            //Don't heartbeat while connecting
            heartbeatTimer.Stop();

            try
            {
                State = new State();

                Client = new Client(endpoint, port, port + 1);
                Client.PacketReceived += Client_PacketWrapperReceived;
                Client.UDPPacketReceived += Client_UDPPacketReceived;
                Client.ServerConnected += Client_ServerConnected;
                Client.ServerFailedToConnect += Client_ServerFailedToConnect;
                Client.ServerDisconnected += Client_ServerDisconnected;

                await Client.Start();
            }
            catch (Exception e)
            {
                Logger.Debug("Failed to connect to server. Retrying...");
                Logger.Debug(e.ToString());
            }
        }

        private Task Client_UDPPacketReceived(PacketWrapper arg)
        {
            throw new NotImplementedException();
        }

        private async Task Client_ServerConnected()
        {
            //Resume heartbeat when connected
            if (shouldHeartbeat) heartbeatTimer.Start();

            await Send(new Packet
            {
                Connect = new Connect
                {
                    ClientType = connectType,
                    Name = username,
                    Password = password ?? "",
                    UserId = userId,
                    ClientVersion = SharedConstructs.VersionCode
                }
            });
        }

        private async Task Client_ServerFailedToConnect()
        {
            //Resume heartbeat if we fail to connect
            //Basically the same as just doing another connect here...
            //But with some extra delay. I don't really know why
            //I'm doing it this way
            if (shouldHeartbeat) heartbeatTimer.Start();

            if (FailedToConnectToServer != null) await FailedToConnectToServer.Invoke(null);
        }

        private async Task Client_ServerDisconnected()
        {
            Logger.Debug("Server disconnected!");
            if (ServerDisconnected != null) await ServerDisconnected.Invoke();
        }

        public void Shutdown()
        {
            Client?.Shutdown();
            heartbeatTimer.Stop();

            //If the Client was connecting when we shut it down, the FailedToConnect event might resurrect the heartbeat without this
            shouldHeartbeat = false;
        }

        public Task Send(Guid id, Packet packet) => Send(new[] { id }, packet);

        public Task Send(Guid[] ids, Packet packet)
        {
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            var forwardedPacket = new ForwardingPacket
            {
                Packet = packet
            };
            forwardedPacket.ForwardToes.AddRange(ids.Select(x => x.ToString()));

            return Forward(forwardedPacket);
        }

        public Task Send(Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            return Client.Send(new PacketWrapper(packet));
        }

        private Task Forward(ForwardingPacket forwardingPacket)
        {
            var packet = forwardingPacket.Packet;
            Logger.Debug($"Forwarding data: {LogPacket(packet)}");
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            return Send(new Packet
            {
                ForwardingPacket = forwardingPacket
            });
        }

        public Task SendUDP(Packet packet)
        {
            Logger.Debug($"Sending data: {LogPacket(packet)}");
            packet.From = Self?.Id ?? Guid.Empty.ToString();
            return Client.SendUDP(new PacketWrapper(packet));
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
                    secondaryInfo = $"{secondaryInfo} from ({user.Id} : {user.Name})";
                }
            }

            return $"({packet.packetCase}) ({secondaryInfo})";
        }

        #region EVENTS/ACTIONS
        public async Task AddUser(User user)
        {
            var @event = new Event
            {
                user_added_event = new Event.UserAddedEvent
                {
                    User = user
                }
            };
            await Send(new Packet
            {
                Event = @event
            });
        }

        private async Task AddUserReceived(User user)
        {
            State.Users.Add(user);

            if (UserConnected != null) await UserConnected.Invoke(user);
        }

        public async Task UpdateUser(User user)
        {
            var @event = new Event
            {
                user_updated_event = new Event.UserUpdatedEvent
                {
                    User = user
                }
            };
            await Send(new Packet
            {
                Event = @event
            });
        }

        public async Task UpdateUserReceived(User user)
        {
            var userToReplace = State.Users.FirstOrDefault(x => x.UserEquals(user));
            State.Users.Remove(userToReplace);
            State.Users.Add(user);

            //If the user updated is *us* (an example of this coming from the outside is stream sync info)
            //we should update our Self
            if (Self.Id == user.Id) Self = user;

            if (UserInfoUpdated != null) await UserInfoUpdated.Invoke(user);
        }

        public async Task RemoveUser(User user)
        {
            var @event = new Event
            {
                user_left_event = new Event.UserLeftEvent
                {
                    User = user
                }
            };
            await Send(new Packet
            {
                Event = @event
            });
        }

        private async Task RemoveUserReceived(User user)
        {
            var userToRemove = State.Users.FirstOrDefault(x => x.UserEquals(user));
            State.Users.Remove(userToRemove);

            if (UserDisconnected != null) await UserDisconnected.Invoke(user);
        }
        #endregion EVENTS/ACTIONS

        protected virtual async Task Client_PacketWrapperReceived(PacketWrapper packet)
        {
            await Client_PacketReceived(packet.Payload);
        }

        protected virtual async Task Client_PacketReceived(Packet packet)
        {
            Logger.Debug($"Recieved data: {LogPacket(packet)}");

            //Ready to go, only disabled since it is currently unusued
            /*if (packet.Type != PacketType.Acknowledgement)
            {
                Send(packet.From, new Packet(new Acknowledgement()
                {
                    PacketId = packet.Id
                }));
            }*/

            if (packet.packetCase == Packet.packetOneofCase.Acknowledgement)
            {
                var acknowledgement = packet.Acknowledgement;
                if (AckReceived != null) await AckReceived.Invoke(acknowledgement, Guid.Parse(packet.From));
            }
            else if (packet.packetCase == Packet.packetOneofCase.Frame)
            {
                var frame = packet.Frame;
                if (FrameReceived != null) await FrameReceived.Invoke(frame);
            }
            else if (packet.packetCase == Packet.packetOneofCase.Event)
            {
                var @event = packet.Event;
                switch (@event.ChangedObjectCase)
                {
                    case Event.ChangedObjectOneofCase.user_added_event:
                        await AddUserReceived(@event.user_added_event.User);
                        break;
                    case Event.ChangedObjectOneofCase.user_updated_event:
                        await UpdateUserReceived(@event.user_updated_event.User);
                        break;
                    case Event.ChangedObjectOneofCase.user_left_event:
                        await RemoveUserReceived(@event.user_left_event.User);
                        break;
                    default:
                        Logger.Error("Unknown command received!");
                        break;
                }
            }
            else if (packet.packetCase == Packet.packetOneofCase.ConnectResponse)
            {
                var response = packet.ConnectResponse;
                if (response.Response.Type == Response.ResponseType.Success)
                {
                    Self = response.Self;
                    State = response.State;
                    if (ConnectedToServer != null) await ConnectedToServer.Invoke(response);
                }
                else if (response.Response.Type == Response.ResponseType.Fail)
                {
                    if (FailedToConnectToServer != null) await FailedToConnectToServer.Invoke(response);
                }
            }
        }
    }
}
