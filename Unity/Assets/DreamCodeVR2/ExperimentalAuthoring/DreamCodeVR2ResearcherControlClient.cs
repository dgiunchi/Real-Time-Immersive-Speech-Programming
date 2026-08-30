using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using DreamCodeVR2.Quest;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class DreamCodeVR2ResearcherControlClient : MonoBehaviour
    {
        [Serializable] public class Response { public string session_id; public string peer_uuid; public string condition; public string questSetId; public string questInstanceId; public string quest_set_id; public string quest_instance_id; public string current_task_id; public QuestInstance questInstance; public QuestInstance quest_instance; public string error; public bool ended; public bool reset; public bool ready; public bool healthy; }
        public StudyConfiguration configuration; public string researcherControlBaseUrl = "http://130.136.2.161:50001"; public int timeoutSeconds = 6;
        public bool IsReachable { get; private set; } public string LastError { get; private set; }
        public string BaseUrl => !string.IsNullOrWhiteSpace(configuration?.researcherControlBaseUrl) ? configuration.researcherControlBaseUrl.TrimEnd('/') : researcherControlBaseUrl.TrimEnd('/');
        public static string ServerCondition(ExperimentCondition condition) => condition == ExperimentCondition.VoiceCommandBaseline ? "voice_command_baseline" : condition == ExperimentCondition.PlayerAuthoring ? "player_authoring" : "dynamic_storytelling";
        public void Health(Action<Response> done) => StartCoroutine(Request("GET", "/api/authoring/dev/health", null, null, done));
        public void GetStatus(string peerUUID, Action<Response> done) => StartCoroutine(Request("GET", "/api/authoring/dev/status/"+UnityWebRequest.EscapeURL(peerUUID), null, peerUUID, done));
        public void StartSession(ExperimentCondition condition, string peerUUID, string questSetId, string questInstanceId, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/start", SessionBody(condition,peerUUID,questSetId,questInstanceId), peerUUID, done));
        public void RestartSession(ExperimentCondition condition, string peerUUID, string questSetId, string questInstanceId, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/restart", SessionBody(condition,peerUUID,questSetId,questInstanceId), peerUUID, done));
        public void EndSession(string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/end", JsonUtility.ToJson(new RequestBody{peerUUID=peerUUID}), peerUUID, done));
        public void ResetSession(string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/reset", JsonUtility.ToJson(new RequestBody{peerUUID=peerUUID}), peerUUID, done));
        [Serializable] private class RequestBody { public string condition; public string peerUUID; public string questSetId; public string questInstanceId; }
        [Serializable] private class C3RequestBody { public string condition; public string peerUUID; }
        private static string SessionBody(ExperimentCondition condition,string peerUUID,string questSetId,string questInstanceId)
        {
            return condition==ExperimentCondition.DynamicStorytelling
                ? JsonUtility.ToJson(new C3RequestBody{condition=ServerCondition(condition),peerUUID=peerUUID})
                : JsonUtility.ToJson(new RequestBody{condition=ServerCondition(condition),peerUUID=peerUUID,questSetId=questSetId,questInstanceId=questInstanceId});
        }
        private IEnumerator Request(string method,string path,string body,string peerUUID,Action<Response> done)
        {
            DreamCodeVR2ClientLogger.Correlate(peerUUID,null,null);DreamCodeVR2ClientLogger.Event("researcher_api","RESEARCHER_HTTP_REQUEST",null,new { method,route=path,peer_uuid=peerUUID });
            using(var request=new UnityWebRequest(BaseUrl+path,method)){request.timeout=timeoutSeconds;if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}request.downloadHandler=new DownloadHandlerBuffer();yield return request.SendWebRequest();var response=new Response();if(request.result!=UnityWebRequest.Result.Success){response.error=request.error;LastError=request.error;IsReachable=false;DreamCodeVR2ClientLogger.Error("researcher_api","RESEARCHER_HTTP_ERROR",request.error,new { route=path,http_status=request.responseCode,peer_uuid=peerUUID });}else{try{response=JsonUtility.FromJson<Response>(request.downloadHandler.text)??response;}catch{response.error="invalid_response";}IsReachable=string.IsNullOrEmpty(response.error);LastError=response.error;DreamCodeVR2ClientLogger.Correlate(peerUUID,response.session_id,null);DreamCodeVR2ClientLogger.Event("researcher_api","RESEARCHER_HTTP_RESPONSE",null,new { route=path,http_status=request.responseCode,peer_uuid=peerUUID,session_id=response.session_id,condition=response.condition,success=IsReachable });}done?.Invoke(response);}
        }
    }
}
