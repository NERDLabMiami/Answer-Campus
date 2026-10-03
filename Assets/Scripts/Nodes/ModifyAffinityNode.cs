namespace VNEngine
{
    public enum AffinityModifyMode { Add_Amount, Set_To }

    public class ModifyAffinityNode : Node
    {
        public Character character = Character.NONE;
        public AffinityModifyMode mode = AffinityModifyMode.Add_Amount;
        public float amount = 0f;

        public override void Run_Node()
        {
            if (character != Character.NONE)
            {
                string key = character.ToString() + "_affinity"; // same key convention as GateAffinityNode
                switch (mode)
                {
                    case AffinityModifyMode.Add_Amount:
                        StatsManager.Add_To_Numbered_Stat(key, amount);
                        break;
                    case AffinityModifyMode.Set_To:
                        StatsManager.Set_Numbered_Stat(key, amount);
                        break;
                }
            }

            Finish_Node();
        }

        public override void Finish_Node()
        {
            StopAllCoroutines();

            base.Finish_Node();
        }
    }
}
