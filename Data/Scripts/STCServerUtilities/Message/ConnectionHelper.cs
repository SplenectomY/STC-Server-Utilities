using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace Splen.ServerUtilities
{
    /// <summary>
    /// Conains useful methods and fields for organizing the connections.
    /// </summary>
    public static class Messaging
    {
        #region fields

        public static List<byte> ClientMessageCache = new List<byte>();
        public static Dictionary<ulong, List<byte>> ServerMessageCache = new Dictionary<ulong, List<byte>>();
        #endregion

        public const ushort ConnectionId = 35243;

        #region connections to server

        /// <summary>
        /// Creates and sends an entity with the given information for the server. Never call this on DS instance!
        /// </summary>

        public static void SendMessageToServer(MessageBase message)
        {
            message.Side = MessageSide.ServerSide;
            if (Sync.IsServer)
                message.SenderSteamId = MyAPIGateway.Multiplayer.ServerId;
            else if (Sync.IsClient)
            {
                if (MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null)
                {
                    MyAPIGateway.Parallel.Start(() =>
                    {
                        VRage.Utils.MyLog.Default.WriteLineAndConsole("SESSION OR PLAYER WAS NULL when sending message to server. Waiting 1 second then resending.");
                        MyAPIGateway.Parallel.Sleep(1000);
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => SendMessageToServer(message));
                    });
                    return;
                }
                message.SenderSteamId = MyAPIGateway.Session.Player.SteamUserId;
                message.SenderDisplayName = MyAPIGateway.Session.Player.DisplayName;
            }
            message.SenderLanguage = (int)MyAPIGateway.Session.Config.Language;
            try
            {
                byte[] byteData = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageToServer(ConnectionId, byteData);
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        #endregion

        #region connections to all

        /// <summary>
        /// Creates and sends an entity with the given information for the server and all players.
        /// </summary>
        /// <param name="message"></param>
        public static void SendMessageToAll(MessageBase message)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                message.SenderSteamId = MyAPIGateway.Multiplayer.ServerId;
            
            if (MyAPIGateway.Session.Player != null)
            {
                message.SenderSteamId = MyAPIGateway.Session.Player.SteamUserId;
                message.SenderDisplayName = MyAPIGateway.Session.Player.DisplayName;
            }
            message.SenderLanguage = (int)MyAPIGateway.Session.Config.Language;

            if (!MyAPIGateway.Multiplayer.IsServer)
                SendMessageToServer(message);
            SendMessageToAllPlayers(message);
        }

        #endregion

        #region connections to clients

        public static void SendMessageToPlayer(ulong steamId, MessageBase message)
        {
            Debug.Write($"SendMessageToPlayer {steamId} {message.Side} {message.GetType().Name}.", 1);

            message.Side = MessageSide.ClientSide;
            try
            {
                byte[] byteData = MyAPIGateway.Utilities.SerializeToBinary(message);
                MyAPIGateway.Multiplayer.SendMessageTo(ConnectionId, byteData, steamId);
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public static void SendMessageToAllPlayers(MessageBase message)
        {
            Debug.Write($"SendMessageToAllPlayers {message.GetType().Name}.", 1);

            try
            {
                message.Side = MessageSide.ClientSide;
                byte[] byteData = MyAPIGateway.Utilities.SerializeToBinary(message);

                List<IMyPlayer> players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, p => p != null && !p.IsBot);
                foreach (IMyPlayer player in players)
                    try{ MyAPIGateway.Multiplayer.SendMessageTo(ConnectionId, byteData, player.SteamUserId); }
                    catch (Exception e) { Debug.HandleException(e); }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public static void SendMessageToPlayerList(MessageBase message, List<ulong> playerSteamIds)
        {
            Debug.Write($"SendMessageToPlayerList {message.GetType().Name}.", 1);

            try
            {
                message.Side = MessageSide.ClientSide;
                byte[] byteData = MyAPIGateway.Utilities.SerializeToBinary(message);

                foreach (var playerSteamID in playerSteamIds)
                    try { MyAPIGateway.Multiplayer.SendMessageTo(ConnectionId, byteData, playerSteamID); }
                    catch (Exception e) { Debug.HandleException(e); }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public static void SendMessageToConcurrentPlayerList(MessageBase message, ConcurrentBag<ulong> playerSteamIds)
        {
            Debug.Write($"SendMessageToPlayerList {message.GetType().Name}.", 1);

            try
            {
                message.Side = MessageSide.ClientSide;
                byte[] byteData = MyAPIGateway.Utilities.SerializeToBinary(message);

                foreach (var playerSteamID in playerSteamIds)
                    try { MyAPIGateway.Multiplayer.SendMessageTo(ConnectionId, byteData, playerSteamID); }
                    catch (Exception e) { Debug.HandleException(e); }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        #endregion

        #region processing

        /// <summary>
        /// Server side execution of the actions defined in the data.
        /// </summary>
        /// <param name="rawData"></param>
        public static void ProcessData(byte[] rawData)
        {
            Debug.Write("Start Message Deserialization", 1);
            MessageBase message;

            try
            { message = MyAPIGateway.Utilities.SerializeFromBinary<MessageBase>(rawData); }
            catch (Exception e)
            {
                Debug.Write($"Message cannot Deserialize. Message length: {rawData.Length}", 1);
                Debug.HandleException(e);
                return;
            }

            Debug.Write("End Message Deserialization", 1);

            if (message != null)
            {
                try
                { message.InvokeProcessing(); }
                catch (Exception e)
                {
                    Debug.Write($"Processing message exception. Side: {message.Side}", 1);
                    Debug.HandleException(e);
                }
            }
        }

        /*
        public static void ProcessInterModData(object data)
        {
            if (data == null)
            {
                EconomyScript.Instance.ServerLogger.WriteVerbose("Message is empty");
                return;
            }

            byte[] byteData = data as byte[];
            if (byteData == null)
            {
                EconomyScript.Instance.ServerLogger.WriteVerbose("Message is invalid format");
                return;
            }

            EconomyScript.Instance.ServerLogger.WriteStart("Start Message Serialization");
            EconInterModBase message;

            try
            {
                message = MyAPIGateway.Utilities.SerializeFromBinary<EconInterModBase>(byteData);
            }
            catch
            {
                EconomyScript.Instance.ServerLogger.WriteError("Message cannot Deserialize");
                return;
            }

            EconomyScript.Instance.ServerLogger.WriteStop("End Message Serialization");

            if (message != null)
            {
                try
                {
                    message.InvokeProcessing();
                }
                catch (Exception e)
                {
                    EconomyScript.Instance.ServerLogger.WriteError("Processing message exception. Exception: {0}", e.ToString());
                }
            }
        }*/

        #endregion
    }
}
