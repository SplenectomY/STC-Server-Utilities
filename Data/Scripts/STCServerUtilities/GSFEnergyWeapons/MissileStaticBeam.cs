using VRage.Game.Components;
using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;
using GSF.Utilities;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace GSF.Weapons.Static
    {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false,
        "LargeStaticLBeamGTF_Large",
        "LargeStaticLBeamGTF_Small",
        "MediumStaticLPulseGTF_Large",
        "MediumStaticLPulseGTF_Small",
        "SSmallBeamStaticGTF_Small")]

    public class StaticBeam : MyGameLogicComponent
    {
        MyObjectBuilder_EntityBase objectBuilder;
        BeamLogic beamLogic;

        private readonly int debug = 1;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;
            var functionalBlock = Entity as IMyFunctionalBlock;           
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