using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Splen;
using Splen.ServerUtilities;
using Sandbox.Definitions;
using Sandbox.Game.Weapons;
using System.Linq;
using System.Threading;

namespace GSF.Utilities
{
    public static class TypeHelpers
    {
        /// <summary> Checks if the given object is of given type. </summary>
        public static bool IsOfType<T>(this object Object, out T Casted) where T : class
        {
            Casted = Object as T;
            return Casted != null;
        }

        public static bool IsOfType<T>(this object Object) where T : class
        {
            return Object is T;
        }
    }

    public class MyCubeBlockEmissive : MyCubeBlock
    { // Class used to set emissives on a block dynamically
        public static void SetEmissiveParts(MyEntity entity, float emissivity, Color emissivePartColor, Color displayPartColor)
        {
            if (entity != null)
                UpdateEmissiveParts(entity.Render.RenderObjectIDs[0], emissivity, emissivePartColor, displayPartColor);
        }
    }

    public class BeamLogic
    {
        private readonly int debug = 0;

        MyDefinitionId m_electricityDefinition = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        MyDefinitionId m_magDefId;
        MyAmmoMagazineDefinition m_magDef;
        MyResourceSinkComponent m_resourceSink;
        MyObjectBuilder_AmmoMagazine m_chargeObjectBuilder;
        SerializableDefinitionId m_chargeDefinitionId;
        IMyCubeBlock m_cubeBlock;
        MyCubeBlock m_myCubeBlock;
        IMyInventory m_inventory;
        IMyFunctionalBlock m_functionalBlock;
        IMyTerminalBlock m_terminalBlock;
        MyShipController m_shipController;
        IMyEntity m_entity;
        public long m_entityId;
        private long _cubeGridEntityId;
        private IMyGunObject<MyGunBase> m_gunObject;
        private MyWeaponDefinition m_weaponDef;
        private float m_timeoutMult;
        private bool m_inventoryCleared = false;
        List<HeatSink> m_gridHeatSinks = new List<HeatSink>();
        List<BeamLogic> m_gridBeamLogics = new List<BeamLogic>();
        public MyCubeGrid CubeGrid;
        private float m_oldPowerConsumption;
        private bool m_initialized;

        bool initInventory;
        public float PowerConsumption = 0.0001f;
        private int m_timer;
        private int m_chargeTimeout;
        private float m_heat;
        float m_operationalPower, m_chargeamount, m_cooling, m_heatPerShot, m_heatMax; 

        int m_chargesInInventory, m_maxAmmo, m_overheatTimer, m_oldAmmoCount;

        public BeamLogic(IMyEntity Entity)
        {
            MyAPIGateway.Parallel.Start(() => Init(Entity));
        }

        private void Init(IMyEntity Entity)
        {
            try
            {
                m_entity = Entity;
                m_entityId = Entity.EntityId;
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
                m_functionalBlock = Entity as IMyFunctionalBlock;
                m_cubeBlock = Entity as IMyCubeBlock;
                m_myCubeBlock = Entity as MyCubeBlock;
                CubeGrid = m_myCubeBlock.CubeGrid;
                _cubeGridEntityId = CubeGrid.EntityId;
                m_terminalBlock = Entity as IMyTerminalBlock;
                m_gunObject = m_cubeBlock as IMyGunObject<MyGunBase>;

                MyWeaponBlockDefinition weaponBlockDef = null; Debug.Write($"def == null ? {weaponBlockDef == null}", 1, debug);
                MyDefinitionManager.Static.TryGetDefinition<MyWeaponBlockDefinition>(new SerializableDefinitionId(m_functionalBlock.BlockDefinition.TypeId, m_functionalBlock.BlockDefinition.SubtypeId), out weaponBlockDef);
                m_weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(weaponBlockDef.WeaponDefinitionId); Debug.Write($"weaponDef == null ? {m_weaponDef == null}", 1, debug);
                Debug.Write($"Attempting to get magDefId from {m_weaponDef.AmmoMagazinesId[0]} ...", 1, debug);
                m_magDefId = m_weaponDef.AmmoMagazinesId[0]; Debug.Write($"Complete. magDefId == null ? {m_magDefId == null}", 1, debug);
                m_magDef = MyDefinitionManager.Static.GetAmmoMagazineDefinition(m_magDefId); Debug.Write($"m_magDef == null ? {m_magDef == null}", 1, debug);
                Debug.Write($"m_weaponDef.WeaponAmmoDatas == null ? {m_weaponDef.WeaponAmmoDatas == null}", 1, debug);
                Debug.Write($"m_weaponDef.WeaponAmmoDatas.Count == {m_weaponDef.WeaponAmmoDatas.Count()}", 1, debug);

                MyWeaponDefinition.MyWeaponAmmoData data = null;
                var i = -1;
                while (i < m_weaponDef.WeaponAmmoDatas.Count() - 1)
                {
                    i++;
                    Debug.Write($"m_weaponDef.WeaponAmmoDatas[{i}] == null ? {m_weaponDef.WeaponAmmoDatas[i] == null }", 1, debug);
                    if (m_weaponDef.WeaponAmmoDatas[i] == null)
                        continue;

                    data = m_weaponDef.WeaponAmmoDatas[i];
                    break;
                }

                m_timeoutMult = 60f / data.RateOfFire;
                m_chargeDefinitionId = m_magDefId;
                Debug.Write($"Attempting to get ammoDef from {m_magDef.AmmoDefinitionId} ...", 1, debug);
                var ammoDef = MyDefinitionManager.Static.GetAmmoDefinition(m_magDef.AmmoDefinitionId);
                Debug.Write($"Complete. ammoDef == null ? {ammoDef == null}", 1, debug);
                var damagePerShot = ammoDef.GetDamageForMechanicalObjects();
                Debug.Write($"Damage per shot = {damagePerShot}", 1, debug);

                m_heatPerShot = (damagePerShot + ammoDef.MaxTrajectory + ammoDef.DesiredSpeed) * 0.01f;
                m_heatMax = m_cubeBlock.SlimBlock.MaxIntegrity;
                Debug.Write($"m_heatMax/m_heatPerShot = {m_heatMax}/{m_heatPerShot} = {m_heatMax / m_heatPerShot} shots", 1, debug);
                m_operationalPower = m_heatPerShot * 0.01f;
                m_maxAmmo = (int)(m_heatMax / m_heatPerShot);

                m_inventory = ((Sandbox.ModAPI.Ingame.IMyTerminalBlock)(Entity)).GetInventory(0) as IMyInventory;
                m_resourceSink = Entity.Components.Get<MyResourceSinkComponent>();

                if (!LogicCore.Instance.BeamLogics.ContainsKey(Entity.EntityId))
                {
                    LogicCore.Instance.BeamLogics.Add(Entity.EntityId, this);
                    Debug.Write("Added new beamlogic to BeamLogics dictionary.", 1, debug);
                }

                MyAPIGateway.Utilities.InvokeOnGameThread(() => RemoveSmokeEffects());
                m_terminalBlock.AppendingCustomInfo += AppendCustomInfo;
                m_initialized = true;
                Debug.Write("Weapon initialization complete.", 1, debug);
            }
            catch (Exception e)
            { 
                Debug.HandleException(e);
                MyAPIGateway.Parallel.Sleep(1000);
                Init(Entity);
            }
        }

        void RemoveSmokeEffects()
        {
            try
            {
                var effect = MyParticlesLibrary.GetParticleEffect("Smoke_Missile");
                if (effect == null) return;

                var generations = effect.GetGenerations();
                if (generations == null) return;

                for (int i = 0; i < generations.Count; ++i) effect.RemoveGeneration(i);
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        public void ClearInventory()
        {
            try
            {
                if (m_inventory != null)
                    m_inventory.RemoveItemsOfType(m_inventory.GetItemAmount(m_chargeDefinitionId), m_magDefId);

                m_inventoryCleared = true;
            }
            catch (Exception e)
            { Debug.HandleException(e); }

        }

        void AppendCustomInfo(IMyTerminalBlock block, StringBuilder info)
        {            
            if (!Sync.IsClient)
                return;

            info.Clear();
            
            info.AppendLine("\n" + m_cubeBlock.DefinitionDisplayNameText);
            info.AppendLine("Power usage: " + (m_operationalPower).ToString("N") + "MW");
            
            if (PowerConsumption < m_operationalPower && PowerConsumption > 0.0001f)
            {
                info.AppendLine("\nWARNING! NOT ENOUGH GRID POWER!");
                info.AppendLine("\nRequired Power: " + (m_operationalPower - PowerConsumption).ToString("N") + "MW");
            }
        }

        private void TryGetShipController()
        {
            try
            {
                foreach (var block2 in CubeGrid.GetFatBlocks())
                    if (block2 != null && block2 is MyShipController)
                    {
                        var newController = block2 as MyShipController;
                        m_shipController = newController;
                        break;
                    }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void CalculatePowerConsumption()
        {
            try
            {
                Debug.Write("Calculating power consumption.", 1, debug);
                if (m_shipController != null)
                { // Computes remaining power of grid, forces gun to not overdraw that power
                    var shipPowerMax = m_shipController.GridResourceDistributor.MaxAvailableResourceByType(MyResourceDistributorComponent.ElectricityId);
                    var shipPowerUsed = m_shipController.GridResourceDistributor.TotalRequiredInputByType(MyResourceDistributorComponent.ElectricityId);
                    var remainingPower = shipPowerMax - shipPowerUsed;
                    remainingPower = (remainingPower < 0f) ? 0f : remainingPower;
                    PowerConsumption = (remainingPower < m_operationalPower) ? (remainingPower / 2) : m_operationalPower;
                }
                else PowerConsumption = m_operationalPower;

                SyncPowerConsumption();

                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    if (m_resourceSink != null && m_electricityDefinition != null)
                        m_resourceSink.SetRequiredInputByType(m_electricityDefinition, PowerConsumption);
                });
                Debug.Write($"Power consumption is {PowerConsumption}.", 1, debug);
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void SyncPowerConsumption(bool force = false)
        {
            if (force || m_oldPowerConsumption != PowerConsumption)
            {
                var message = new MessageWeaponPowerUpdate();
                message.EntityId = m_entityId;
                message.PowerConsumption = PowerConsumption;
                m_oldPowerConsumption = PowerConsumption;

                if (!Core.Instance.CubeGridInfo.ContainsKey(_cubeGridEntityId))
                    Messaging.SendMessageToAllPlayers(message);
                else
                    Messaging.SendMessageToConcurrentPlayerList(message, Core.Instance.CubeGridInfo[_cubeGridEntityId].PlayersInSyncRange);  
            }
        }

        private void NoPowerNeeded(bool sync = false)
        {
            PowerConsumption = 0.0001f;
            m_chargeamount = 0f;
            MyAPIGateway.Utilities.InvokeOnGameThread(() => 
            {
                try
                {
                    if (m_resourceSink != null && m_electricityDefinition != null)
                        m_resourceSink.SetRequiredInputByType(m_electricityDefinition, PowerConsumption);
                }
                catch (Exception e)
                { Debug.HandleException(e); }
            });
            

            if (sync) SyncPowerConsumption();
        }

        private void RechargeAmmo()
        {
            try
            {
                if (Debug.Write($"Weapon heat: {m_heat}/{m_heatMax}", 1, debug) && m_heat > m_heatMax)
                    return;

                var ammoMissing = m_maxAmmo - (int)m_inventory.GetItemAmount(m_chargeDefinitionId);
                var ammoAdded = 0;
                while (m_chargeamount >= m_operationalPower && m_inventory != null && ammoAdded < ammoMissing && m_heat <= m_heatMax)
                {
                    m_chargeTimeout = m_timer + (int)(60 * m_timeoutMult);
                    ammoAdded++;
                    var heat = m_heat + m_heatPerShot; m_heat = heat;
                    var charge = m_chargeamount - m_operationalPower; m_chargeamount = charge;
                }
                if (Debug.Write($"Adding {ammoAdded} energy ammo rounds to weapon.", 1, debug) && ammoAdded > 0)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        if (m_inventory != null)
                            m_inventory.AddItems(ammoAdded, (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(m_magDefId));
                    });
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void SendOverheatedMessageToPlayer(string message, string color, int warningLevel)
        {
            try
            {
                foreach (var item in Core.Instance.playerWeaponWarningLevels[m_cubeBlock.CubeGrid.EntityId].blockWarnings.ToList())
                    if (item.Key != m_cubeBlock.EntityId && warningLevel <= item.Value)
                        return;

                MyAPIGateway.Utilities.InvokeOnGameThread(() => Core.Instance.playerWeaponWarningLevels[m_cubeBlock.CubeGrid.EntityId].blockWarnings[m_cubeBlock.EntityId] = warningLevel);

                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, x => x != null && x.Controller != null && x.Controller.ControlledEntity != null && x.Client != null && Debug.Write("Found a valid, online player", 2, debug));

                foreach (var player in players)
                {
                    var playerEntity = player.Controller.ControlledEntity.Entity;
                    IMyShipController SC;
                    if (Debug.Write($"playerEntity == null ? {playerEntity == null}", 2, debug) && playerEntity != null && playerEntity.IsOfType(out SC) && SC.CubeGrid == m_cubeBlock.CubeGrid)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                if (Debug.Write($"player == null ? {player == null}", 2, debug) && player != null)
                                {
                                    /* WORK ON THIS ADD CUSTOM GUI PLEASE
                                    if (!Core.Instance.LastWeaponHeatNoteSentToPlayer.ContainsKey(player.IdentityId))
                                        Core.Instance.LastWeaponHeatNoteSentToPlayer.Add(player.IdentityId, MyVisualScriptLogicProvider.AddNotification(message, color, player.IdentityId));
                                    else
                                    {
                                        MyVisualScriptLogicProvider.RemoveNotification(Core.Instance.LastWeaponHeatNoteSentToPlayer[player.IdentityId], player.IdentityId);
                                        Core.Instance.LastWeaponHeatNoteSentToPlayer[player.IdentityId] = MyVisualScriptLogicProvider.AddNotification(message, color, player.IdentityId);
                                    }

                                    if (!Core.Instance.TimeWeaponNoteWasSentToPlayer.ContainsKey(player.IdentityId))
                                        Core.Instance.TimeWeaponNoteWasSentToPlayer.Add(player.IdentityId, Core.Instance.Timer);
                                    else Core.Instance.TimeWeaponNoteWasSentToPlayer[player.IdentityId] = Core.Instance.Timer;
                                    */

                                    MyVisualScriptLogicProvider.ClearNotifications(player.IdentityId);
                                    MyVisualScriptLogicProvider.ShowNotification(message, 3000, color, player.IdentityId);
                                }

                                // TO DO: play some audio
                                // make sure the audio is on a cooldown so we dont get spam
                            }
                            catch (Exception e)
                            { Debug.HandleException(e); }
                        });
                    }
                }
            }
            catch (Exception e)
            { Debug.HandleException(e); }
        }

        private void UpdateGridWarningDictionaries()
        {
            try
            {
                if (!Core.Instance.playerWeaponWarningLevels.ContainsKey(m_myCubeBlock.CubeGrid.EntityId))
                    Core.Instance.playerWeaponWarningLevels.Add(m_myCubeBlock.CubeGrid.EntityId, new WeaponHeatWarningGrid(m_myCubeBlock.CubeGrid));

                var gridWarning = Core.Instance.playerWeaponWarningLevels[m_myCubeBlock.CubeGrid.EntityId];

                if (gridWarning.blockWarnings.ContainsKey(m_myCubeBlock.EntityId))
                    gridWarning.blockWarnings[m_myCubeBlock.EntityId] = 0;
                else if (!gridWarning.blockWarnings.ContainsKey(m_myCubeBlock.EntityId))
                    gridWarning.blockWarnings.Add(m_myCubeBlock.EntityId, 0);
            }
            catch (Exception e)
            { VRage.Utils.MyLog.Default.WriteLine(e.ToString()); }
        }

        private void SendHeatNotifications()
        {
            try
            {
                var heat = (m_heat + ((m_maxAmmo - (int)m_inventory.GetItemAmount(m_chargeDefinitionId)) * m_heatPerShot)) / m_heatMax;
                var heatPercent = (int)(heat * 100);

                if (heat > 0.8f && heat <= 1f && Debug.Write("Sending weapons overheating message ...", 1, debug))
                    MyAPIGateway.Parallel.Start(() => SendOverheatedMessageToPlayer($"WARNING: Weapons overheating! ({heatPercent}%)", "White", heatPercent));
                else if (m_heat > m_heatMax && Debug.Write("Sending weapons overheated message ...", 1, debug))
                    MyAPIGateway.Parallel.Start(() => SendOverheatedMessageToPlayer($"WARNING: Weapons overheated! ({heatPercent}%)", "Red", heatPercent));
            }
            catch(Exception e)
            { Debug.HandleException(e); }
        }

        public void Update100()
        {
            if (!m_initialized) return;
            Debug.Write("BeamLogic.Update100 running.", 1, debug);

            if (Sync.IsClient && MyAPIGateway.Gui?.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            { // Force terminal screen to refresh if being viewed
                m_functionalBlock.RefreshCustomInfo();
                LogicCore.Instance.UpdateTerminal(m_myCubeBlock);
            }

            if (!m_functionalBlock.Enabled || !m_functionalBlock.IsFunctional || !m_functionalBlock.IsWorking)
            {
                Debug.Write("Setting power level to 0 due to nonfunctional block.", 1, debug);
                NoPowerNeeded();
                if (Sync.IsServer)
                {
                    if (m_heat > 0f) m_heat = Math.Max(m_heat - 1f, 0f);
                    if (!m_inventoryCleared) ClearInventory();
                    m_timer += 100;
                }
                return;
            }

            if (Sync.IsClient) m_resourceSink.SetRequiredInputByType(m_electricityDefinition, PowerConsumption);

            if (!Sync.IsServer) return;

            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    Debug.Write("Beginning parallel processing.", 1, debug);
                    var newheat = m_heat - 100f; m_heat = Math.Max(newheat, 0f);
                    Interlocked.Add(ref m_timer, 100);

                    m_inventoryCleared = false;

                    Debug.Write($"Timeout? {!(m_timer > m_chargeTimeout)} | Ammo max? {(int)m_inventory.GetItemAmount(m_chargeDefinitionId) >= m_maxAmmo} | Overheated? {m_heat > m_heatMax}", 1, debug);
                    if (m_timer > m_chargeTimeout && ((int)m_inventory.GetItemAmount(m_chargeDefinitionId) >= m_maxAmmo || m_heat > m_heatMax))
                    {
                        Debug.Write("No power needed to continue.", 1, debug);
                        NoPowerNeeded(true);
                        if (m_heat > 0f)
                            MyAPIGateway.Parallel.Start(() =>
                            {
                                LogicCore.Instance.MoveHeatIntoSinks(ref m_heat, m_myCubeBlock);
                                SendHeatNotifications();
                            });
                        return;
                    }

                    CalculatePowerConsumption();

                    if (m_heat > m_heatMax) return;

                    var newCharge = m_chargeamount + (PowerConsumption * 10);
                    m_chargeamount = newCharge;

                    Debug.Write($"m_chargeamount = {m_chargeamount}. m_operationalPower = {m_operationalPower}", 1, debug);

                    if (m_chargeamount > m_operationalPower && Debug.Write("Charge amount was greater than operational power. Charging ammo.", 1, debug))
                        RechargeAmmo();

                    if (m_heat > 0f)
                    {
                        LogicCore.Instance.MoveHeatIntoSinks(ref m_heat, m_myCubeBlock);
                        SendHeatNotifications();
                    }

                    if (m_shipController == null) TryGetShipController();
                }
                catch (Exception e)
                { Debug.HandleException(e); } 
            }, () => 
            UpdateGridWarningDictionaries());
        }
    }
}