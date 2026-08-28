using System;
using System.IO;
using Newtonsoft.Json;
using Ubiq.Rooms;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public sealed class DreamCodeVR2ClientLogger : MonoBehaviour
    {
        [Serializable] private class Entry { public string timestamp; public string level; public string source="client"; public string peer_uuid; public string session_id; public string condition; public string subsystem; public string @event; public string message; public object details; }
        public static DreamCodeVR2ClientLogger Instance { get; private set; }
        public static string LogDirectory => Path.Combine(Application.persistentDataPath,"DreamCodeVR2","logs");
        public bool IsActive { get; private set; } public string CurrentLogFilename { get; private set; } public string LastEvent { get; private set; } public int WarningCount { get; private set; } public int ErrorCount { get; private set; }
        private readonly object sync=new object(); private StreamWriter writer; private string peerUuid; private string sessionId; private string condition; private bool configured; private bool handlingUnityLog;

        private void Awake(){if(Instance&&Instance!=this){Destroy(this);return;}Instance=this;DontDestroyOnLoad(gameObject);Initialize();Application.logMessageReceivedThreaded+=OnUnityLog;}
        private void OnDestroy(){if(Instance==this){Application.logMessageReceivedThreaded-=OnUnityLog;Flush();writer?.Dispose();Instance=null;}}
        private void OnApplicationPause(bool pause){if(pause)Flush();}
        private void OnApplicationFocus(bool focus){if(!focus)Flush();}
        private void OnApplicationQuit(){Flush();}
        public void Configure(StudyConfiguration configuration){configured=configuration==null||configuration.enableFileLogging;Correlate(null,null,configuration?.condition);Event("bootstrap","CONFIG_LOADED",null,new { ubiq_host=configuration?.ubiqServerHost,ubiq_port=configuration?.ubiqServerPort,researcher_base_url=configuration?.researcherControlBaseUrl,verbose_network_logging=configuration?.verboseNetworkLogging });}
        private void Initialize(){try{Directory.CreateDirectory(LogDirectory);CurrentLogFilename="client_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"_run.jsonl";writer=new StreamWriter(new FileStream(Path.Combine(LogDirectory,CurrentLogFilename),FileMode.Append,FileAccess.Write,FileShare.Read)){AutoFlush=true};configured=true;Write("INFO","bootstrap","LOGGER_INITIALIZED",null,new { log_file=CurrentLogFilename },false);Write("INFO","bootstrap","APP_START",null,new { persistent_data_path=Application.persistentDataPath },false);}catch{configured=false;}}
        public static void Correlate(string peer=null,string session=null,ExperimentCondition? studyCondition=null){Instance?.SetCorrelation(peer,session,studyCondition);}
        private void SetCorrelation(string peer,string session,ExperimentCondition? studyCondition){lock(sync){if(!string.IsNullOrEmpty(peer))peerUuid=peer;if(!string.IsNullOrEmpty(session))sessionId=session;if(studyCondition.HasValue)condition=DreamCodeVR2ResearcherControlClient.ServerCondition(studyCondition.Value);}}
        public static void Event(string subsystem,string eventName,string message=null,object details=null)=>Instance?.Write("INFO",subsystem,eventName,message,details,true);
        public static void Warn(string subsystem,string eventName,string message=null,object details=null)=>Instance?.Write("WARN",subsystem,eventName,message,details,true);
        public static void Error(string subsystem,string eventName,string message=null,object details=null)=>Instance?.Write("ERROR",subsystem,eventName,message,details,true);
        public static void MarkTest(){Event("researcher","RESEARCHER_TEST_MARK");}
        public void Flush(){lock(sync){try{writer?.Flush();}catch{}}}
        private void Write(string level,string subsystem,string eventName,string message,object details,bool context){if(!configured||writer==null)return;lock(sync){try{if(level=="WARN")WarningCount++;if(level=="ERROR")ErrorCount++;LastEvent=eventName;var entry=new Entry{timestamp=DateTime.UtcNow.ToString("o"),level=level,peer_uuid=context?peerUuid:null,session_id=context?sessionId:null,condition=context?condition:null,subsystem=subsystem,@event=eventName,message=message,details=details};writer.WriteLine(JsonConvert.SerializeObject(entry,Formatting.None));if(level=="ERROR")writer.Flush();}catch{}}}
        private void OnUnityLog(string message,string stackTrace,LogType type){if(handlingUnityLog||type==LogType.Log&&string.IsNullOrEmpty(message))return;handlingUnityLog=true;try{var level=type==LogType.Error||type==LogType.Exception||type==LogType.Assert?"ERROR":type==LogType.Warning?"WARN":"INFO";Write(level,"unity","UNITY_"+type.ToString().ToUpperInvariant(),message,string.IsNullOrEmpty(stackTrace)?null:new { stack_trace=stackTrace },false);}finally{handlingUnityLog=false;}}
    }

    public class DreamCodeVR2UbiqDiagnostics : MonoBehaviour
    {
        private RoomClient roomClient; private string lastPeerUuid;
        private void Start(){roomClient=FindFirstObjectByType<RoomClient>();var joiner=FindFirstObjectByType<global::RoomJoiner>();DreamCodeVR2ClientLogger.Event("ubiq","ROOM_JOIN_REQUESTED",null,new { room_guid=joiner?joiner.Guid:null });if(roomClient){roomClient.OnJoinedRoom.AddListener(room=>DreamCodeVR2ClientLogger.Event("ubiq","ROOM_JOINED",null,new { room_uuid=room.UUID,room_name=room.Name,join_code=room.JoinCode }));roomClient.OnJoinRejected.AddListener(rejection=>DreamCodeVR2ClientLogger.Error("ubiq","ROOM_JOIN_ERROR",rejection.reason));}}
        private void Update()
        {
            if(!roomClient)roomClient=FindFirstObjectByType<RoomClient>();
            var peer=roomClient?.Me?.uuid;
            if(string.IsNullOrEmpty(peer)||peer==lastPeerUuid)return;
            var changed=!string.IsNullOrEmpty(lastPeerUuid);
            var previous=lastPeerUuid;
            lastPeerUuid=peer;
            DreamCodeVR2ClientLogger.Correlate(peer);
            DreamCodeVR2ClientLogger.Event("ubiq",changed?"UBIQ_PEER_UUID_CHANGED":"PEER_UUID_AVAILABLE",null,new { peer_uuid=peer,previous_peer_uuid=previous });
            if(changed)
            {
                FindFirstObjectByType<ExperimentConditionManager>()?.InvalidateResearcherSessionReady();
                DreamCodeVR2ClientLogger.Warn("session","SESSION_RESTART_REQUIRED_AFTER_UBIQ_RECONNECT","Ubiq assigned a new peer UUID; press START to establish the corresponding researcher session.",new { peer_uuid=peer });
            }
        }
    }
}
