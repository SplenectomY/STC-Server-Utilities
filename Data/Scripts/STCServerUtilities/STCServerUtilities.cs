using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.Definitions.SessionComponents;
using VRage.Game.ObjectBuilders.Definitions.SessionComponents;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using VRage.Game.ModAPI;
using VRage.Game;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;
using Ingame = Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.Game.Components;

namespace Splen.ServerUtilities
{
    //public static class Splen{
        //public static List<long> activeFactories = new List<long>();
        //public static int factoryScanTimeout = 0;
        //public static int toolProcessWait = 0;
    //}
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class OnInit : MySessionComponentBase
    {

		public bool m_initialize = false;

        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            if (!m_initialize)
            {
                m_initialize = true;

                //fix missing PCUs
                foreach (var def in MyDefinitionManager.Static.GetAllDefinitions())
                {

                    var bd = def as MyCubeBlockDefinition;
                    if(bd == null)
                        continue;

                    string idString = string.Format("{0}", bd.Id);

                    if (idString.Contains("MyObjectBuilder_LargeGatlingTurret") && bd.PCU == 1) bd.PCU = 225; 
                    else if(idString.Contains("MyObjectBuilder_LargeMissileTurret") && bd.PCU == 1) bd.PCU = 275; 
                    else if(idString.Contains("MyObjectBuilder_InteriorTurret") && bd.PCU == 1) bd.PCU = 125;

                    else if(idString.Contains("MyObjectBuilder_AdvancedDoor/VCZ_Elevator")) bd.PCU = 125; 
                    else if(idString.Contains("MyObjectBuilder_AdvancedDoor/LargeShipUsableLadderRetractable")) bd.PCU = 125;
                    else if(idString.Contains("MyObjectBuilder_AdvancedDoor/SmallShipUsableLadderRetractable")) bd.PCU = 125;
                    else if(idString.Contains("MyObjectBuilder_AdvancedDoor")) bd.PCU = 50;

                    else if(idString.Contains("MyObjectBuilder_AirVent")) bd.PCU = 10;
                    else if(idString.Contains("MyObjectBuilder_Assembler")) bd.PCU = 40;
                    else if(idString.Contains("MyObjectBuilder_BatteryBlock")) bd.PCU = 15;
                    else if(idString.Contains("MyObjectBuilder_ButtonPanel")) bd.PCU = 5;
                    else if(idString.Contains("MyObjectBuilder_CameraBlock")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_CargoContainer")) bd.PCU = 10;
                    else if(idString.Contains("MyObjectBuilder_Cockpit")) bd.PCU = 15;
                    else if(idString.Contains("MyObjectBuilder_ConveyorSorter")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_Conveyor")) bd.PCU = 10;
                    else if(idString.Contains("MyObjectBuilder_Door")) bd.PCU = 115; 
                    else if(idString.Contains("MyObjectBuilder_Drill")) bd.PCU = 190;
                    else if(idString.Contains("MyObjectBuilder_ExtendedPistonBase")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_Gyro")) bd.PCU = 50;
                    else if(idString.Contains("MyObjectBuilder_InteriorLight")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_LandingGear")) bd.PCU = 35; 
                    else if(idString.Contains("MyObjectBuilder_MotorAdvancedRotor")) bd.PCU = 100; 

                    else if(idString.Contains("MyObjectBuilder_OreDetector/NaniteUltrasonicHammer")) bd.PCU = 225; 
                    else if(idString.Contains("MyObjectBuilder_OreDetector")) bd.PCU = 40;

                    else if(idString.Contains("MyObjectBuilder_OxygenGenerator")) bd.PCU = 40;
                    else if(idString.Contains("MyObjectBuilder_OxygenTank")) bd.PCU = 25; 
                    else if(idString.Contains("MyObjectBuilder_PistonTop")) bd.PCU = 5;
                    else if(idString.Contains("MyObjectBuilder_Projector")) bd.PCU = 50;
                    else if(idString.Contains("MyObjectBuilder_RadioAntenna")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_Reactor")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_Refinery")) bd.PCU = 90;
                    else if(idString.Contains("MyObjectBuilder_ReflectorLight")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_RemoteControl")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_ShipGrinder")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_ShipWelder")) bd.PCU = 150;
                    else if(idString.Contains("MyObjectBuilder_SmallGatlingGun")) bd.PCU = 80;

                    else if(idString.Contains("MyObjectBuilder_SmallMissileLauncher") && bd.PCU == 1) bd.PCU = 425;

                    else if(idString.Contains("MyObjectBuilder_SolarPanel")) bd.PCU = 55;
                    else if(idString.Contains("MyObjectBuilder_TextPanel")) bd.PCU = 5;

                    else if(idString.Contains("MyObjectBuilder_Thrust")&& bd.PCU == 1) bd.PCU = 15; 

                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/Radar")) bd.PCU = 250;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/DSControl")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/Emitter")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/LargeEnhancer")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/LargeShieldModulator")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/LargeHackingBlock")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/LargeFirewallBlock")) bd.PCU = 25;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/MA_Navball")) bd.PCU = 25; 
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/PlanetaryEmitterLarge")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/SmallEnhancer")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/SmallHackingBlock")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_UpgradeModule/SmallShieldModulator")) bd.PCU = 100;

                    else if(idString.Contains("MyObjectBuilder_Warhead/ThermoNuclearLargeWarhead")) bd.PCU = 100;
                    else if(idString.Contains("MyObjectBuilder_Warhead/ThermoNuclearSmallWarhead")) bd.PCU = 50;

                    else MyLog.Default.WriteLine(string.Format("{0} | PCU: {1}", bd.Id, bd.PCU));
                    
                }
                
                return;
            }

        }

        
        int cooldown = 0;
        public override void UpdateAfterSimulation()
        {
            cooldown++;
            if(cooldown < 600) return;
            cooldown = 0;
            var identities = new List<IMyIdentity>();
            MyAPIGateway.Players.GetAllIdentites(identities);
            foreach (var identity in identities) 
            {
                MyLog.Default.WriteLine(string.Format("{0} | PCU: {1}", identity.DisplayName, identity.BlockLimits.PCU));
            }
        }
	}
}