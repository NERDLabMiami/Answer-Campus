using UnityEngine;
using VNEngine;

public class NodeFootballInvite : Node
{
    public Character character;

    public override void Run_Node()
    {
        StatsManager.Set_Boolean_Stat(character.ToString().ToLower() + "_football_invite_accepted", true);
        Finish_Node();
    }

    public override void Finish_Node()
    {
        StopAllCoroutines();
        base.Finish_Node();
    }
}
