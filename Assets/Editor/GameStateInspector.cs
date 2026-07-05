using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VNEngine;

public class GameStateInspector : EditorWindow
{
    [MenuItem("Window/Answer Campus/Game State")]
    public static void Open()
        => GetWindow<GameStateInspector>(false, "Game State");

    private Vector2 _scrollPos;
    private int     _weekSlider   = 1;
    private bool    _isNight      = false;
    private string  _setStatName  = "";
    private string  _setStatValue = "";
    private Stat    _setStatType  = Stat.Numbered_Stat;

    private bool _showNumbered = true;
    private bool _showBoolean  = false;
    private bool _showString   = false;
    private bool _showItems    = false;

    private void Update()
    {
        if (Application.isPlaying) Repaint();
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Answer Campus — Game State Inspector", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to read/write live stats.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawControls();
        EditorGUILayout.Space(6);
        DrawStatsDump();

        EditorGUILayout.EndScrollView();
    }

    private void DrawControls()
    {
        EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

        // Week slider
        EditorGUI.BeginChangeCheck();
        _weekSlider = EditorGUILayout.IntSlider("Week", _weekSlider, 1, 16);
        if (EditorGUI.EndChangeCheck())
            StatsManager.Set_Numbered_Stat("Week", _weekSlider);

        // IsNight toggle
        EditorGUI.BeginChangeCheck();
        _isNight = EditorGUILayout.Toggle("Is Night", _isNight);
        if (EditorGUI.EndChangeCheck())
            StatsManager.Set_Numbered_Stat("IsNight", _isNight ? 1f : 0f);

        EditorGUILayout.Space(4);

        // Quick jump
        EditorGUILayout.LabelField("Quick Jump", EditorStyles.miniBoldLabel);
        GUILayout.BeginHorizontal();
        foreach (int w in new[] { 1, 7, 9, 16 })
        {
            if (GUILayout.Button($"Week {w}"))
            {
                StatsManager.Set_Numbered_Stat("Week", w);
                _weekSlider = w;
            }
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Set arbitrary stat
        EditorGUILayout.LabelField("Set Stat", EditorStyles.miniBoldLabel);
        _setStatType  = (Stat)EditorGUILayout.EnumPopup("Type",  _setStatType);
        _setStatName  = EditorGUILayout.TextField("Name",  _setStatName);
        _setStatValue = EditorGUILayout.TextField("Value", _setStatValue);
        if (GUILayout.Button("Apply")) ApplyStat();

        EditorGUILayout.Space(4);

        // Actions
        EditorGUILayout.LabelField("Actions", EditorStyles.miniBoldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Load Latest Save"))   LoadLatestSave();
        if (GUILayout.Button("Save Game"))          TriggerSave();
        if (GUILayout.Button("Reset: First Day"))   ResetToFirstDay();
        GUILayout.EndHorizontal();

        // Season record readout
        EditorGUILayout.Space(4);
        var (wins, losses) = FootballScheduler.GetSeasonRecord();
        EditorGUILayout.LabelField($"Football Record:  {wins}W – {losses}L", EditorStyles.miniLabel);
    }

    private void DrawStatsDump()
    {
        EditorGUILayout.LabelField("Live Stats", EditorStyles.boldLabel);

        _showNumbered = EditorGUILayout.Foldout(_showNumbered,
            $"Numbered ({StatsManager.numbered_stats.Count})", true);
        if (_showNumbered)
        {
            foreach (var kv in StatsManager.numbered_stats)
                EditorGUILayout.LabelField(kv.Key, kv.Value.ToString("F2"), EditorStyles.miniLabel);
        }

        _showBoolean = EditorGUILayout.Foldout(_showBoolean,
            $"Boolean ({StatsManager.boolean_stats.Count})", true);
        if (_showBoolean)
        {
            foreach (var kv in StatsManager.boolean_stats)
                EditorGUILayout.LabelField(kv.Key, kv.Value.ToString(), EditorStyles.miniLabel);
        }

        _showString = EditorGUILayout.Foldout(_showString,
            $"String ({StatsManager.string_stats.Count})", true);
        if (_showString)
        {
            foreach (var kv in StatsManager.string_stats)
            {
                string val = kv.Value?.Length > 80 ? kv.Value.Substring(0, 77) + "..." : kv.Value;
                EditorGUILayout.LabelField(kv.Key, val, EditorStyles.miniLabel);
            }
        }

        _showItems = EditorGUILayout.Foldout(_showItems,
            $"Items ({StatsManager.items.Count})", true);
        if (_showItems)
        {
            foreach (var item in StatsManager.items)
                EditorGUILayout.LabelField(item, EditorStyles.miniLabel);
        }
    }

    private void ApplyStat()
    {
        if (string.IsNullOrEmpty(_setStatName)) return;
        switch (_setStatType)
        {
            case Stat.Numbered_Stat:
                if (float.TryParse(_setStatValue, out float f))
                    StatsManager.Set_Numbered_Stat(_setStatName, f);
                break;
            case Stat.Boolean_Stat:
                if (bool.TryParse(_setStatValue, out bool b))
                    StatsManager.Set_Boolean_Stat(_setStatName, b);
                break;
            case Stat.String_Stat:
                StatsManager.Set_String_Stat(_setStatName, _setStatValue);
                break;
        }
    }

    private void LoadLatestSave()
    {
        SaveManager.LoadFromFile();
        var saves = SaveManager.saved_games;
        if (saves == null || saves.Count == 0)
        {
            Debug.LogWarning("[GameStateInspector] No saves found.");
            return;
        }
        var latest = saves[saves.Count - 1];
        if (latest.saved_numbered_stats != null)
            StatsManager.numbered_stats = new Dictionary<string, float>(latest.saved_numbered_stats);
        if (latest.saved_boolean_stats != null)
            StatsManager.boolean_stats  = new Dictionary<string, bool>(latest.saved_boolean_stats);
        if (latest.saved_string_stats != null)
            StatsManager.string_stats   = new Dictionary<string, string>(latest.saved_string_stats);
        _weekSlider = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("Week"));
        _isNight    = StatsManager.Get_Numbered_Stat("IsNight") > 0.5f;
        Debug.Log("[GameStateInspector] Latest save loaded into live StatsManager.");
    }

    private void TriggerSave()
    {
        var snap = new SaveFile();
        snap.saved_numbered_stats = new Dictionary<string, float>(StatsManager.numbered_stats);
        snap.saved_boolean_stats  = new Dictionary<string, bool>(StatsManager.boolean_stats);
        snap.saved_string_stats   = new Dictionary<string, string>(StatsManager.string_stats);
        SaveManager.AddNewSave(snap);
        Debug.Log("[GameStateInspector] Snapshot saved.");
    }

    private void ResetToFirstDay()
    {
        StatsManager.numbered_stats.Clear();
        StatsManager.boolean_stats.Clear();
        StatsManager.string_stats.Clear();
        StatsManager.items.Clear();
        StatsManager.Set_Numbered_Stat("Week", 1);
        StatsManager.Set_Numbered_Stat("IsNight", 0);
        FootballScheduler.GenerateSchedule();
        GameEvents.EnsureSemesterRequiredEventsRegistered();
        _weekSlider = 1;
        _isNight    = false;
        Debug.Log("[GameStateInspector] Reset to first day.");
    }
}
