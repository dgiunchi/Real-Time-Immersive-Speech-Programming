using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DreamCodeVR2.ExperimentalAuthoring
{
    public class DreamCodeVR2ResearcherControlClient : MonoBehaviour
    {
        [Serializable] public class Response { public string session_id; public string peer_uuid; public string condition; public string error; public bool ended; public bool reset; public bool ready; public bool healthy; }
        public StudyConfiguration configuration; public string researcherControlBaseUrl = "http://127.0.0.1:3004"; public int timeoutSeconds = 6;
        public bool IsReachable { get; private set; } public string LastError { get; private set; }
        public string BaseUrl => !string.IsNullOrWhiteSpace(configuration?.researcherControlBaseUrl) ? configuration.researcherControlBaseUrl.TrimEnd('/') : researcherControlBaseUrl.TrimEnd('/');
        public static string ServerCondition(ExperimentCondition condition) => condition == ExperimentCondition.VoiceCommandBaseline ? "voice_command_baseline" : condition == ExperimentCondition.PlayerAuthoring ? "player_authoring" : "dynamic_storytelling";
        public void Health(Action<Response> done) => StartCoroutine(Request("GET", "/api/authoring/dev/health", null, done));
        public void GetStatus(string peerUUID, Action<Response> done) => StartCoroutine(Request("GET", "/api/authoring/dev/status/"+UnityWebRequest.EscapeURL(peerUUID), null, done));
        public void StartSession(ExperimentCondition condition, string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/start", JsonUtility.ToJson(new RequestBody{condition=ServerCondition(condition),peerUUID=peerUUID}), done));
        public void RestartSession(ExperimentCondition condition, string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/restart", JsonUtility.ToJson(new RequestBody{condition=ServerCondition(condition),peerUUID=peerUUID}), done));
        public void EndSession(string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/end", JsonUtility.ToJson(new RequestBody{peerUUID=peerUUID}), done));
        public void ResetSession(string peerUUID, Action<Response> done) => StartCoroutine(Request("POST", "/api/authoring/dev/session/reset", JsonUtility.ToJson(new RequestBody{peerUUID=peerUUID}), done));
        [Serializable] private class RequestBody { public string condition; public string peerUUID; }
        private IEnumerator Request(string method,string path,string body,Action<Response> done)
        {
            using(var request=new UnityWebRequest(BaseUrl+path,method)){request.timeout=timeoutSeconds;if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}request.downloadHandler=new DownloadHandlerBuffer();yield return request.SendWebRequest();var response=new Response();if(request.result!=UnityWebRequest.Result.Success){response.error=request.error;LastError=request.error;IsReachable=false;}else{try{response=JsonUtility.FromJson<Response>(request.downloadHandler.text)??response;}catch{response.error="invalid_response";}IsReachable=string.IsNullOrEmpty(response.error);LastError=response.error;}done?.Invoke(response);}
        }
    }
}
