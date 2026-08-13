using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class ExperimentTelemetry : MonoBehaviour
    {
        public ExperimentConditionManager conditionManager; public AuthoringProtocolClient protocolClient; public bool writeLocalJsonLines = true;
        private string path; private void Start(){if(!conditionManager)conditionManager=FindFirstObjectByType<ExperimentConditionManager>();if(!protocolClient)protocolClient=FindFirstObjectByType<AuthoringProtocolClient>();path=Path.Combine(Application.persistentDataPath,"dreamcodevr_experiment_events.jsonl");}
        public void Log(string eventType,string objectId=null,string actionId=null,bool success=true,float latency=0f)
        { var e=new ExperimentEvent{timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),participantCode=conditionManager?.participantCode,sessionId=conditionManager?.sessionId,condition=conditionManager?.condition.ToString(),questId=conditionManager?.questId,questVariant=conditionManager?.questVariant,eventType=eventType,objectIds=string.IsNullOrEmpty(objectId)?Array.Empty<string>():new[]{objectId},actionId=actionId,success=success,latency=latency};if(writeLocalJsonLines)File.AppendAllText(path,JsonUtility.ToJson(e)+Environment.NewLine);protocolClient?.SendExperimentEvent(e); }
    }
}
