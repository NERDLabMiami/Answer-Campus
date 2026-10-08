using UnityEditor;
using VNEngine;

[CustomEditor(typeof(ModifyAffinityNode))]
public class ModifyAffinityNodeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();

        var myScript = target as ModifyAffinityNode;

        VNNodeEditorGUI.DrawCategoryBanner(VNNodeEditorGUI.NodeCategory.DataStats);

        myScript.character = (Character)EditorGUILayout.EnumPopup("Character", myScript.character);
        myScript.mode = (AffinityModifyMode)EditorGUILayout.EnumPopup("Mode", myScript.mode);
        myScript.amount = EditorGUILayout.FloatField(
            myScript.mode == AffinityModifyMode.Add_Amount ? "Add Amount" : "Set To",
            myScript.amount);

        if (myScript.character == Character.NONE)
            EditorGUILayout.HelpBox("Select a character — NONE will do nothing when this node runs.", MessageType.Warning);

        VNNodeEditorGUI.MarkDirtyIfChanged(myScript, EditorGUI.EndChangeCheck());
    }
}
