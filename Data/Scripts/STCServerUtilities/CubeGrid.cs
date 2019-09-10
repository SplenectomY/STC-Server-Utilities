using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Splen.ServerUtilities
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public class CubeGrid : MyGameLogicComponent
    {
        public ConcurrentBag<ulong> PlayersInSyncRange = new ConcurrentBag<ulong>();
        private int _timer;
        private IMyCubeGrid _cubeGrid;
        private long _entityId;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _cubeGrid = Entity as IMyCubeGrid;
            _entityId = Entity.EntityId;

            if (Sync.IsClient) NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;

            if (Sync.IsServer) NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

            if (!Core.Instance.CubeGridInfo.ContainsKey(_entityId))
                Core.Instance.CubeGridInfo.Add(_entityId, this);
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();
            var message = new MessageNewClientInGridSyncRange();
            message.EntityId = _entityId;
            Messaging.SendMessageToServer(message);
        }

        public override void UpdateBeforeSimulation100()
        {
            if (_timer++ % 36 == 0) // every minute
                MyAPIGateway.Parallel.Start(() => GetPlayersInSyncRange());
        }

        public override void Close()
        {
            Core.Instance.CubeGridInfo.Remove(_entityId);
            base.Close();
        }

        private void GetPlayersInSyncRange()
        {
            if (_cubeGrid == null) return;

            try
            {
                ulong steamID = 0;
                while (!PlayersInSyncRange.IsEmpty)
                    PlayersInSyncRange.TryTake(out steamID);

                var syncDistance = MyAPIGateway.Session.SessionSettings.SyncDistance;
                var players = new List<IMyPlayer>();

                MyAPIGateway.Players.GetPlayers(players, (x) => x != null && !x.IsBot);

                foreach (var player in players)
                    if (Vector3D.Distance(player.GetPosition(), _cubeGrid.GetPosition()) < syncDistance)
                        PlayersInSyncRange.Add(player.SteamUserId);
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }
    }
}