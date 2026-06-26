using System;
using System.Collections;
using System.Text;
using Ubiq.Messaging;
using Ubiq.Networking;
using Ubiq.Rooms;
using UnityEngine;
using UnityEngine.Networking;
using static System.Net.WebRequestMethods;

namespace DreamCodeVR2.Quest
{
    public class QuestPlannerClient : MonoBehaviour
    {
        [Header("Server")]
        //public string serverBaseUrl = "http://localhost:3002";
        public string serverBaseUrl = "http://130.136.2.161:50001";
        public string endpointPath = "/api/quest/generate";
        public string defaultMode = "llm_generated_v1";
        public string defaultTemplate = string.Empty;
        public int requestTimeoutSeconds = 10;

        [Header("Peer Identity")]
        public string playerPrefsPeerUuidKey = "DreamCodeVR2.QuestPlannerClient.PeerUuid";

        private RoomClient roomClient;
        private string cachedLocalPeerUuid;

        public void RequestQuestPlan(string mode, string template, Action<bool, QuestPlan, string> onComplete)
        {
            StartCoroutine(RequestQuestPlanCoroutine(mode, template, onComplete));
        }

        public string GetEffectivePeerUuid()
        {
            EnsureRoomClient();
            if (roomClient != null && roomClient.Me != null && !string.IsNullOrWhiteSpace(roomClient.Me.uuid))
            {
                return roomClient.Me.uuid;
            }

            if (!string.IsNullOrWhiteSpace(cachedLocalPeerUuid))
            {
                return cachedLocalPeerUuid;
            }

            cachedLocalPeerUuid = PlayerPrefs.GetString(playerPrefsPeerUuidKey, string.Empty);
            if (string.IsNullOrWhiteSpace(cachedLocalPeerUuid))
            {
                cachedLocalPeerUuid = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(playerPrefsPeerUuidKey, cachedLocalPeerUuid);
                PlayerPrefs.Save();
            }

            return cachedLocalPeerUuid;
        }

        private IEnumerator RequestQuestPlanCoroutine(string mode, string template, Action<bool, QuestPlan, string> onComplete)
        {
            var effectiveMode = string.IsNullOrWhiteSpace(mode) ? defaultMode : mode.Trim();
            var effectiveTemplate = string.IsNullOrWhiteSpace(template) ? defaultTemplate : template.Trim();
            var requestUrl = BuildRequestUrl();
            var peerUuid = GetEffectivePeerUuid();

            var payload = new QuestPlannerRequest
            {
                peerUUID = string.IsNullOrWhiteSpace(peerUuid) ? "test-peer" : peerUuid,
                mode = string.IsNullOrWhiteSpace(effectiveMode) ? "llm_generated_v1" : effectiveMode
            };

            if (payload.mode == "debug_template" && !string.IsNullOrWhiteSpace(effectiveTemplate))
            {
                payload.template = effectiveTemplate;
            }

            var requestJson = JsonUtility.ToJson(payload);
            Debug.Log($"[QuestPlannerClient] POST {requestUrl} mode={payload.mode} template={payload.template ?? string.Empty}");

            using (var request = new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST))
            {
                var bodyBytes = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    var error = $"status={(long)request.responseCode} error={request.error}";
                    Debug.LogWarning($"[QuestPlannerClient] Request failed {error}");
                    onComplete?.Invoke(false, null, error);
                    yield break;
                }

                var responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    const string error = "status=200 error=Empty response body";
                    Debug.LogWarning($"[QuestPlannerClient] Request failed {error}");
                    onComplete?.Invoke(false, null, error);
                    yield break;
                }

                QuestPlan plan = null;
                try
                {
                    plan = JsonUtility.FromJson<QuestPlan>(responseText);
                }
                catch (Exception exception)
                {
                    var error = $"status={(long)request.responseCode} error={exception.Message}";
                    Debug.LogWarning($"[QuestPlannerClient] Request failed {error}");
                    onComplete?.Invoke(false, null, error);
                    yield break;
                }

                if (plan == null)
                {
                    var error = $"status={(long)request.responseCode} error=QuestPlan deserialized to null";
                    Debug.LogWarning($"[QuestPlannerClient] Request failed {error}");
                    onComplete?.Invoke(false, null, error);
                    yield break;
                }

                Debug.Log($"[QuestPlannerClient] Received quest {plan.quest_id} title=\"{plan.title}\"");
                onComplete?.Invoke(true, plan, null);
            }
        }

        private void EnsureRoomClient()
        {
            if (!roomClient)
            {
                roomClient = NetworkScene.Find(this)?.GetComponentInChildren<RoomClient>();
            }
        }

        private string BuildRequestUrl()
        {
            var baseUrl = string.IsNullOrWhiteSpace(serverBaseUrl) ? "http://localhost:3002" : serverBaseUrl.Trim().TrimEnd('/');
            var path = string.IsNullOrWhiteSpace(endpointPath) ? "/api/quest/generate" : endpointPath.Trim();
            if (!path.StartsWith("/"))
            {
                path = "/" + path;
            }

            return baseUrl + path;
        }

        [Serializable]
        private class QuestPlannerRequest
        {
            public string peerUUID;
            public string mode;
            public string template;
        }
    }
}
