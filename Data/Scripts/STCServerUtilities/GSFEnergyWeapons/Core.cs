using VRage.Game.Components;
using VRage.Game;
using System.Collections.Generic;
using GSF.Utilities;
using Sandbox.Game.Entities;
using System.Linq;
using System;
using Splen.ServerUtilities;
using Sandbox.ModAPI;

namespace GSF
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class LogicCore : MySessionComponentBase
    {
        public static LogicCore Instance;
        public bool FinishedInit;
        public Dictionary<long, List<HeatSink>> GridHeatsinkSystems = new Dictionary<long, List<HeatSink>>();
        public Dictionary<long, BeamLogic> BeamLogics = new Dictionary<long, BeamLogic>();

        /// <summary>
        /// Should be run inside a parallel task. Scans heatsinks on the same grid as cubeBlock and transfers heat if room is available.
        /// </summary>
        /// <param name="heat"></param>
        /// <param name="cubeBlock"></param>
        public void MoveHeatIntoSinks(ref float heat, MyCubeBlock cubeBlock)
        {
            try
            {
                if (heat <= 0f || cubeBlock == null || cubeBlock.CubeGrid == null || !Instance.GridHeatsinkSystems.ContainsKey(cubeBlock.CubeGrid.EntityId))
                    return;

                foreach (var sink in Instance.GridHeatsinkSystems[cubeBlock.CubeGrid.EntityId].ToList())
                {
                    if (heat <= 0f) break;

                    if (sink.CubeBlock == null)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => Instance.GridHeatsinkSystems[cubeBlock.CubeGrid.EntityId].Remove(sink));
                        continue;
                    }

                    if (sink.Heat >= sink.MaxHeat) continue;

                    var sinkCapacityRemaining = sink.MaxHeat - sink.Heat;
                    var maxSinkAmt = sinkCapacityRemaining > 100f ? 100f : sinkCapacityRemaining;
                    var sinkUsed = heat > sinkCapacityRemaining ? sinkCapacityRemaining : heat;
                    var newheat = Math.Max(heat - sinkUsed, 0f); heat = newheat;
                    var newSinkHeat = sink.Heat + sinkUsed; sink.Heat = newSinkHeat;
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public void UpdateTerminal(MyCubeBlock cubeBlock)
        {
            if (cubeBlock == null || cubeBlock.IDModule == null)
                return;

            MyOwnershipShareModeEnum sharemode = cubeBlock.IDModule.ShareMode;

            cubeBlock.ChangeOwner(cubeBlock.IDModule.Owner, sharemode == MyOwnershipShareModeEnum.None ? MyOwnershipShareModeEnum.Faction : MyOwnershipShareModeEnum.None);
            cubeBlock.ChangeOwner(cubeBlock.IDModule.Owner, sharemode);
        }

        public LogicCore()
        {
            Instance = this;
        }
        
        public override void Init(MyObjectBuilder_SessionComponent objectBuilder)
        {
            base.Init(objectBuilder);
            FinishedInit = true;
        }
    }
}
