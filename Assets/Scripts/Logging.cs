using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using OGD;
using System.Text;
using VNEngine;

public class Logging : MonoBehaviour
{
    public string appId;
    public int appVersion;
    public int clientLogVersion;
    public bool debugMode = true;

    private OGDLog m_Logger;
    public static Logging Instance { get; private set; }
    public bool IsReady() => m_Logger != null && m_Logger.IsReady();
    public void LogCustomEvent(
        string eventName,
        IDictionary<string,string> str = null,
        IDictionary<string,int>    ints = null,
        IDictionary<string,float>  floats = null,
        IDictionary<string,bool>   bools = null,
        IDictionary<string,string> jsonPayloads = null // key -> already-serialized JSON
    )
    {
        if (!IsReady()) { Debug.Log("OGD Logger Not Ready..."); return; }
        m_Logger.BeginEvent(eventName);
        if (str   != null) foreach (var kv in str)   m_Logger.EventParam(kv.Key, kv.Value);
        if (ints  != null) foreach (var kv in ints)  m_Logger.EventParam(kv.Key, kv.Value);
        if (floats!= null) foreach (var kv in floats)m_Logger.EventParam(kv.Key, kv.Value);
        if (bools != null) foreach (var kv in bools) m_Logger.EventParam(kv.Key, kv.Value);
        if (jsonPayloads != null) foreach (var kv in jsonPayloads) m_Logger.EventParamJson(kv.Key, kv.Value);
        m_Logger.SubmitEvent();
    }

    // 2) Stats snapshot under one JSON key (e.g., "stats")
    public void LogStatsSnapshot(string eventName,
                                 string jsonKey = "stats",
                                 IEnumerable<string> numberKeys = null,
                                 IEnumerable<string> boolKeys = null,
                                 IEnumerable<string> stringKeys = null,
                                 IDictionary<string,object> extraContext = null)
    {
        if (!IsReady()) { Debug.Log("OGD Logger Not Ready..."); return; }

        // Build nested JSON manually so we don't rely on Unity's JsonUtility for dictionaries.
        var root = new Dictionary<string, object>();
        var stats = new Dictionary<string, object>();
        var n = new Dictionary<string, object>();
        var b = new Dictionary<string, object>();
        var s = new Dictionary<string, object>();

        // If a key-list is null => include all of that category
        var allNum  = numberKeys ?? VNEngine.StatsManager.numbered_stats.Keys.ToList();
        var allBool = boolKeys   ?? VNEngine.StatsManager.boolean_stats.Keys.ToList();
        var allStr  = stringKeys ?? VNEngine.StatsManager.string_stats.Keys.ToList();

        foreach (var k in allNum)  if (VNEngine.StatsManager.numbered_stats.ContainsKey(k)) n[k] = VNEngine.StatsManager.numbered_stats[k];
        foreach (var k in allBool) if (VNEngine.StatsManager.boolean_stats.ContainsKey(k))  b[k] = VNEngine.StatsManager.boolean_stats[k];
        foreach (var k in allStr)  if (VNEngine.StatsManager.string_stats.ContainsKey(k))  s[k] = VNEngine.StatsManager.string_stats[k];

        if (n.Count>0) stats["numbered"] = n;
        if (b.Count>0) stats["boolean"]  = b;
        if (s.Count>0) stats["string"]   = s;

        root[jsonKey] = stats;
        if (extraContext != null) root["context"] = extraContext;

        // Tiny JSON writer
        var sb = new System.Text.StringBuilder();
        WriteJson(sb, root);

        m_Logger.BeginEvent(eventName);
        m_Logger.EventParamJson(jsonKey, sb.ToString());
        if (extraContext != null) m_Logger.EventParamJson("context", ToJson(extraContext));
        m_Logger.SubmitEvent();

        // --- helpers
        static string ToJson(IDictionary<string,object> d) { var sb2=new System.Text.StringBuilder(); WriteJson(sb2,d); return sb2.ToString(); }
        static void WriteJson(System.Text.StringBuilder sb, object obj)
        {
            switch (obj)
            {
                case null: sb.Append("null"); break;
                case string s0: sb.Append('"').Append(Escape(s0)).Append('"'); break;
                case bool b0: sb.Append(b0 ? "true" : "false"); break;
                case float f0: sb.Append(f0.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case double d0: sb.Append(d0.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case int i0: sb.Append(i0); break;
                case long l0: sb.Append(l0); break;
                case IDictionary<string, object> dict:
                    sb.Append('{'); bool first=true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(", "); first=false;
                        sb.Append('"').Append(Escape(kv.Key)).Append("\": "); WriteJson(sb, kv.Value);
                    }
                    sb.Append('}'); break;
                case IEnumerable<object> list0:
                    sb.Append('['); first=true;
                    foreach (var v in list0)
                    {
                        if (!first) sb.Append(", "); first=false; WriteJson(sb, v);
                    }
                    sb.Append(']'); break;
                default:
                    if (obj is IEnumerable<string> sa) { sb.Append('['); bool f=true; foreach(var s in sa){ if(!f) sb.Append(", "); f=false; sb.Append('"').Append(Escape(s??"")).Append('"'); } sb.Append(']'); }
                    else if (obj is float || obj is double || obj is int || obj is long) sb.Append(obj.ToString());
                    else sb.Append('"').Append(Escape(obj.ToString())).Append('"');
                    break;
            }
        }
        static string Escape(string s) => s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t");
    }
    // Logging.cs
    public void BeginCustom(string eventName)
    {
        if (m_Logger == null || !m_Logger.IsReady()) return;    // guard
        m_Logger.BeginEvent(eventName);
    }
    public void Param(string key, string val)  { if (m_Logger != null && m_Logger.IsReady()) m_Logger.EventParam(key, val); }
    public void Param(string key, int val)     { if (m_Logger != null && m_Logger.IsReady()) m_Logger.EventParam(key, val); }
    public void Param(string key, float val)   { if (m_Logger != null && m_Logger.IsReady()) m_Logger.EventParam(key, val); }
    public void Param(string key, bool val)    { if (m_Logger != null && m_Logger.IsReady()) m_Logger.EventParam(key, val); }
    public void ParamJson(string key, string json) { if (m_Logger != null && m_Logger.IsReady()) m_Logger.EventParamJson(key, json); }
    public void SubmitCustom()                 { if (m_Logger != null && m_Logger.IsReady()) m_Logger.SubmitEvent(); }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Read appId from steam_appid.txt
            string appIdPath = Application.dataPath + "/../steam_appid.txt";
            if (System.IO.File.Exists(appIdPath))
            {
                appId = System.IO.File.ReadAllText(appIdPath).Trim();
            }
            else
            {
                Debug.LogError("steam_appid.txt not found!");
            }
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private IEnumerator Start()
    {
        m_Logger = new OGDLog(appId, appVersion);
        m_Logger.SetUserId("default");
        m_Logger.SetDebug(debugMode);

        while (!m_Logger.IsReady())
            yield return null;

        Debug.LogFormat("current session id: {0}", m_Logger.GetSessionId().ToString("X16"));

        m_Logger.GameState("{\"platform\":16}");

        var sb = new StringBuilder();
        sb.Append("{\"platform\":16}");
        m_Logger.GameState(sb);

    }


    public void LogPlayerChoice(string choice, int choiceIndex, Dictionary<int, string> order, List<int> randomizedOrder)
    {
        if(m_Logger.IsReady()) {
            m_Logger.BeginEvent("playerChoice");
            m_Logger.EventParam("choice", choice);
            m_Logger.EventParam("choiceIndex", choiceIndex); 
            m_Logger.EventParamJson("order", JsonUtility.ToJson(order));
            m_Logger.EventParamJson("random_order", JsonUtility.ToJson(randomizedOrder));
            m_Logger.SubmitEvent();
        }
        else
        {
            Debug.Log("OGD Logger Not Ready...");
        }
    }

}
