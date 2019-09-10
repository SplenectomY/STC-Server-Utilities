using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using GSF.Utilities;
using VRage.ModAPI;

namespace GSF.GSFPulseCannons
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false, 
    "SSPhotonLauncher_Small", "SSDualPlasmaStatic_Small", "LSDualPlasmaStatic_Large", 
    "NovaTorpedoLauncher_Large", "ThorStatic_Large", "ThorStatic_Small")] 

	public class SmallPlasmaMissileLauncher : MyGameLogicComponent
	{
        MyObjectBuilder_EntityBase objectBuilder;
        BeamLogic beamLogic;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;
            beamLogic = new BeamLogic(Entity);
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = true)
        {
            return objectBuilder;
        }

        public override void UpdateBeforeSimulation100()
        {
            beamLogic.Update100();
        }

        public override void MarkForClose()
        {
            beamLogic.ClearInventory();
            base.MarkForClose();
        }

        public override void Close()
        {
            beamLogic.ClearInventory();
            base.Close();
        }
    }
}