using System;
using System.Collections.Generic;
using System.Linq;
using GSF;
using GSF.Utilities;
using ProtoBuf;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Splen.ServerUtilities
{
    public enum MessageSide
    {
        ServerSide,
        ClientSide
    }

    [ProtoContract]
    // ALL CLASSES DERIVED FROM MessageBase MUST BE ADDED HERE
    [ProtoInclude(1, typeof(MessageThrusterVariables))]
    [ProtoInclude(2, typeof(MessageWeaponPowerUpdate))]
    [ProtoInclude(3, typeof(MessageHeatSinkUpdate))]
    [ProtoInclude(4, typeof(MessageAsteroidSync))] 
    [ProtoInclude(5, typeof(MessageBeginAsteroidProcessing))]
    [ProtoInclude(6, typeof(MessageOpenParachute))]
    [ProtoInclude(7, typeof(MessageNewClientInGridSyncRange))]

    public abstract class MessageBase
    {
        private const int debug = 0;

        /// <summary>
        /// The SteamId of the message's sender. Note that this will be set when the message is sent, so there is no need for setting it otherwise.
        /// </summary>
        [ProtoMember(101)]
        public ulong SenderSteamId;

        /// <summary>
        /// The display name of the message sender.
        /// </summary>
        [ProtoMember(102)]
        public string SenderDisplayName;

        /// <summary>
        /// The current display language of the sender.
        /// </summary>
        [ProtoMember(103)]
        public int SenderLanguage;

        /// <summary>
        /// Defines on which side the message should be processed. Note that this will be set when the message is sent, so there is no need for setting it otherwise.
        /// </summary>
        [ProtoMember(104)]
        public MessageSide Side;

        public MessageBase()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                SenderSteamId = MyAPIGateway.Multiplayer.ServerId;
            if (MyAPIGateway.Session.Player != null)
                SenderSteamId = MyAPIGateway.Session.Player.SteamUserId;
        }

        /*
        [ProtoAfterDeserialization]
        void InvokeProcessing() // is not invoked after deserialization from xml
        {
            EconomyScript.Instance.ServerLogger.Write("START - Processing");
            switch (Side)
            {
                case MessageSide.ClientSide:
                    ProcessClient();
                    break;
                case MessageSide.ServerSide:
                    ProcessServer();
                    break;
            }
            EconomyScript.Instance.ServerLogger.Write("END - Processing");
        }
        */

        public void InvokeProcessing()
        {
            switch (Side)
            {
                case MessageSide.ClientSide:
                    if (Sync.IsClient)
                        InvokeClientProcessing();
                    break;
                case MessageSide.ServerSide:
                    if (Sync.IsServer)
                        InvokeServerProcessing();
                    break;
            }
        }

        private void InvokeClientProcessing()
        {
            Debug.Write($"Client Received - {this.GetType().Name}", 1, debug);
            try
            { ProcessClient(); }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void InvokeServerProcessing()
        {
            Debug.Write($"Server Received - {this.GetType().Name}", 1, debug);
            try
            { ProcessServer(); }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public abstract void ProcessClient();
        public abstract void ProcessServer();
    }

    [ProtoContract]
    public class MessageThrusterVariables : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public float ThrustMultiplier;

        [ProtoMember(11)]
        public long EntityId;

        [ProtoMember(12)]
        public float Overclock;

        [ProtoMember(13)]
        public bool SafetySwitch;

        [ProtoMember(14)]
        public bool UpdateCustomData;

        public override void ProcessClient()
        {
            Process();
        }

        public override void ProcessServer()
        {
            Process();
        }

        private void Process()
        {
            IMyEntity entity;
            MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity);

            if (Debug.Write($"entity == null {entity == null}", 1, debug) && entity == null)
                return;

            var termBlock = entity as IMyTerminalBlock;

            if (Debug.Write($"termBlock == null {termBlock == null}", 1, debug) && termBlock == null)
                return;

            var logic = termBlock.GameLogic.GetAs<ThrustOverride>();

            if (Debug.Write($"logic == null {logic == null}", 1, debug) && logic == null)
                return;

            if (ThrustMultiplier != 0f)
                logic.CurrentMultiplier = ThrustMultiplier;

            logic.UpdateCustomData = UpdateCustomData;
            logic.Overclock = Overclock;
            logic.SafetySwitch = SafetySwitch;
            Debug.Write($"Processed MessageThrusterVariables on {Side.ToString()} successfully.", 1, debug);
        }

    }

    [ProtoContract]
    public class MessageWeaponPowerUpdate : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public float PowerConsumption;

        [ProtoMember(11)]
        public long EntityId;

        public override void ProcessClient()
        {
            Debug.Write($"Client received MessageWeaponPowerUpdate", 1, debug);
            BeamLogic beamLogic = null;

            if (LogicCore.Instance.BeamLogics.ContainsKey(EntityId))
                beamLogic = LogicCore.Instance.BeamLogics[EntityId];
            
            if (Debug.Write($"MessageWeaponPowerUpdate: beamLogic == null ? {beamLogic == null}", 1, debug) && beamLogic != null)
            {
                beamLogic.PowerConsumption = PowerConsumption;
                Debug.Write($"Processed MessageWeaponPowerUpdate on Client successfully. Client powerconsumption updated to {PowerConsumption}", 1, debug);
            }
        }

        public override void ProcessServer()
        {

        }
    } 

    [ProtoContract]
    public class MessageHeatSinkUpdate : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public float Heat;

        [ProtoMember(11)]
        public long EntityId;

        public override void ProcessClient()
        {
            IMyEntity entity;
            MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity);

            if (Debug.Write($"entity == null {entity == null}", 2, debug) && entity == null)
                return;

            var termBlock = entity as IMyTerminalBlock;

            if (Debug.Write($"termBlock == null {termBlock == null}", 2, debug) && termBlock == null)
                return;

            var logic = termBlock.GameLogic.GetAs<HeatSink>();

            if (Debug.Write($"logic == null {logic == null}", 2, debug) && logic == null)
                return;

            logic.Heat = Heat;

            Debug.Write("Processed MessageHeatSinkUpdate on Client successfully.", 1, debug);
        }

        public override void ProcessServer()
        {

        }
    }

    [ProtoContract]
    public class MessageAsteroidSync : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public double X;

        [ProtoMember(11)]
        public double Y;

        [ProtoMember(12)]
        public double Z;

        [ProtoMember(13)]
        public double Radius;

        public override void ProcessClient()
        {
            var zone = new BoundingSphereD(new Vector3D(X, Y, Z), Radius);

            Core.Instance.AsteroidSafeZones.Add(zone);
            Debug.Write($"Client {MyAPIGateway.Session.Player.DisplayName} - {MyAPIGateway.Session.Player.SteamUserId} received asteroid safe zone.", 1, debug);
        }

        public override void ProcessServer()
        {
            Debug.Write($"Client {SenderDisplayName} - {SenderSteamId} requested asteroid safe zones from server.", 1, debug);
            try
            {
                SendZoneInfo();
            }
            catch (Exception e)
            { 
                Debug.HandleException(e);
                Debug.Write("SendZoneInfo interupted!!! Attempting to send again.", 1, debug);
                SendZoneInfo();
            }
        }

        private void SendZoneInfo()
        {
            int count = Core.Instance.AsteroidSafeZones.Count;
            foreach (var zone in Core.Instance.AsteroidSafeZones.ToList())
            {
                var message = new MessageAsteroidSync();
                message.X = zone.Center.X;
                message.Y = zone.Center.Y;
                message.Z = zone.Center.Z;
                message.Radius = zone.Radius;
                message.Side = MessageSide.ClientSide;
                Debug.Write($"Server is sending asteroid zone data to {SenderDisplayName} - {SenderSteamId}.", 1, debug);
                Messaging.SendMessageToPlayer(SenderSteamId, message);
            }
            Messaging.SendMessageToPlayer(SenderSteamId, new MessageBeginAsteroidProcessing());
        }
    }

    [ProtoContract]
    public class MessageBeginAsteroidProcessing : MessageBase
    {
        private const int debug = 0;

        public override void ProcessClient()
        {
            Core.Instance.InitAsteroidRestrictions();
            Debug.Write($"Client {MyAPIGateway.Session.Player.DisplayName} - {MyAPIGateway.Session.Player.SteamUserId} received all asteroid safe zones and hooked event.", 1, debug);
        }

        public override void ProcessServer()
        {

        }
    }

    [ProtoContract]
    public class MessageShowOverheatNotification : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public double X;

        [ProtoMember(11)]
        public double Y;

        [ProtoMember(12)]
        public double Z;

        [ProtoMember(13)]
        public double Radius;

        public override void ProcessClient()
        {
            Core.Instance.InitAsteroidRestrictions();
            Debug.Write($"Client {MyAPIGateway.Session.Player.DisplayName} - {MyAPIGateway.Session.Player.SteamUserId} received all asteroid safe zones and hooked event.", 1, debug);
        }

        public override void ProcessServer()
        {

        }
    }

    [ProtoContract]
    public class MessageOpenParachute : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(13)]
        public long EntityId;

        public override void ProcessClient()
        {
            var entity = MyAPIGateway.Entities.GetEntityById(EntityId); if (!(entity is IMyCubeBlock)) return;
            var cubeBlock = entity as IMyCubeBlock; if (cubeBlock == null) return;
            var chute = cubeBlock as IMyParachute; if (chute == null) return;

            chute.OpenDoor();
        }

        public override void ProcessServer()
        {

        }
    }

    [ProtoContract]
    public class MessageNewClientInGridSyncRange : MessageBase
    {
        private const int debug = 0;

        [ProtoMember(10)]
        public long EntityId;

        public override void ProcessClient()
        {

        }

        public override void ProcessServer()
        {
            MyAPIGateway.Parallel.Start(() => 
            {
                try
                {
                    if (Core.Instance.CubeGridInfo.ContainsKey(EntityId))
                        Core.Instance.CubeGridInfo[EntityId].PlayersInSyncRange.Add(SenderSteamId);
                }
                catch(Exception e)
                { Debug.HandleException(e); }
            }); 
        }
    }
}
