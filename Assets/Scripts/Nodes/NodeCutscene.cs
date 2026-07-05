using System.Collections;
using UnityEngine;
using VNEngine;

// Triggers navigation to a new scene, optionally saving first.
// All overlay / transition visuals are owned by HomeCutsceneController in Home.unity.
public class NodeCutscene : Node
{
    [Header("Navigation")]
    [Tooltip("Scene to load. Leave empty to just save and advance the conversation.")]
    public string targetScene;

    [Header("Save")]
    [Tooltip("Call SaveManager.Save() before navigating.")]
    public bool triggerSave = false;

    public override void Run_Node()
    {
        StartCoroutine(PlayCutscene());
    }

    private IEnumerator PlayCutscene()
    {
        if (triggerSave)
        {
            SaveManager.Save();
            yield return new WaitForSeconds(0.6f);
        }

        if (!string.IsNullOrEmpty(targetScene))
        {
            LocationRouter.Go(targetScene);
        }
        else
        {
            Finish_Node();
        }
    }

    public override void Finish_Node()
    {
        StopAllCoroutines();
        base.Finish_Node();
    }
}
