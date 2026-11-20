using System;
using System.Collections.Generic;

namespace Nullroute.Core
{
    // All persistent state lives in these plain classes: public fields and Lists only,
    // so both Unity's JsonUtility and System.Text.Json (headless sim) can serialize them.

    public enum NodeKind { Relay, Training, Corporate, Bank, Research, University, Police, Medical, Media }

    public enum MissionType { Steal, Delete, Plant, Alter }

    public enum ToolId { PwCrack, FwBypass, PxBypass, MonBypass, Decrypt, LogWipe, Spoofer }

    public enum HwId { Cpu, Ram, Nic, Storage, Cloak }

    [Serializable]
    public class ToolLevel
    {
        public ToolId id;
        public int version;
    }

    [Serializable]
    public class LocalFile
    {
        public string name;
        public int size;
        public int encryption;
        public string fromNode;
        public int missionId;
    }

    [Serializable]
    public class FileData
    {
        public string name;
        public int size;
        public int encryption;
    }

    [Serializable]
    public class FieldData
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class RecordData
    {
        public string id;
        public string name;
        public List<FieldData> fields = new List<FieldData>();
    }

    [Serializable]
    public class LogEntry
    {
        public double time;
        public string text;
        public bool byPlayer;
        public bool suspicious;
    }

    [Serializable]
    public class NodeData
    {
        public string id;
        public string ip;
        public string name;
        public NodeKind kind;
        public int tier;
        public bool isPublic;
        public bool known;

        // Security layers: 0 = not present, otherwise the level a tool must match.
        public int proxy;
        public int firewall;
        public int monitor;
        public int password;

        /// <summary>Seconds for an unshielded, direct trace to complete.</summary>
        public double traceTime;

        // Session flags (reset on disconnect).
        public bool proxyDown;
        public bool firewallDown;
        public bool monitorDown;

        // Persistent.
        public bool passwordKnown;
        public string adminPassword;
        public double breachedAt;

        public List<FileData> files = new List<FileData>();
        public List<RecordData> records = new List<RecordData>();
        public List<LogEntry> logs = new List<LogEntry>();
    }

    [Serializable]
    public class MissionData
    {
        public int id;
        public MissionType type;
        public int tier;
        public string nodeId;
        public string fileName;
        public int fileSize;
        public string recordId;
        public string personName;
        public string field;
        public string value;
        public long payout;
        public int rep;
        public double expires;
        public string client;
        public bool tutorial;
    }

    [Serializable]
    public class InvestigationData
    {
        public string nodeId;
        public double ends;
        public double chance;
    }

    [Serializable]
    public class PlayerData
    {
        public string handle;
        public string gatewayIp;
        public long credits;
        public int rep;
        public int jobsDone;
        public long totalEarned;
        public int nodesBreached;

        public int cpu = 1;
        public int ram = 1;
        public int nic = 1;
        public int storage = 1;
        public int cloak = 1;

        public List<ToolLevel> tools = new List<ToolLevel>();
        public List<LocalFile> files = new List<LocalFile>();
        public List<string> bounces = new List<string>();
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public PlayerData player = new PlayerData();
        public List<NodeData> nodes = new List<NodeData>();
        public List<MissionData> board = new List<MissionData>();
        public List<MissionData> active = new List<MissionData>();
        public List<InvestigationData> investigations = new List<InvestigationData>();
        public int nextId = 1;
        public double time;
        public int tutorialStep;
        public bool tutorialDone;
        public string trainingNodeId;
        public int tutorialMissionId;
        public bool tutorialMissionDone;
    }

    public interface ISaveStore
    {
        bool Exists();
        SaveData Load();
        void Save(SaveData data);
        void Delete();
    }

    public enum ProcKind { Crack, FwBypass, PxBypass, MonBypass, LogWipe, Download, Upload, Delete, DbEdit, Decrypt }

    /// <summary>A running background task (not persisted).</summary>
    public class Proc
    {
        public int pid;
        public ProcKind kind;
        public string label;
        public double duration;
        public double elapsed;
        public string nodeId; // null for local processes
        public string arg;
        public string arg2;
        public string arg3;

        public double Progress { get { return duration <= 0 ? 1 : Math.Min(1, elapsed / duration); } }
        public double Remaining { get { return Math.Max(0, duration - elapsed); } }
    }
}
