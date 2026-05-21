using System.Collections;
using Ubiq.Messaging;
using Ubiq.Rooms;
using UnityEngine;

public class RoomJoiner : MonoBehaviour
{
    public string Guid;
    public float retrySeconds = 1.0f;
    public bool logJoinAttempts = true;

    private RoomClient roomClient;
    private NetworkScene networkScene;

    private void Start()
    {
        roomClient = GetComponent<RoomClient>();
        networkScene = NetworkScene.Find(this);

        roomClient.OnJoinedRoom.AddListener(room =>
        {
            if (logJoinAttempts)
            {
                Debug.Log($"[RoomJoiner] joined room name={room.Name} uuid={room.UUID} joincode={room.JoinCode}");
            }
        });

        roomClient.OnJoinRejected.AddListener(rejection =>
        {
            Debug.LogWarning($"[RoomJoiner] join rejected reason={rejection.reason}");
        });

        StartCoroutine(JoinWhenConnected());
    }

    private IEnumerator JoinWhenConnected()
    {
        if (!System.Guid.TryParse(Guid, out var roomGuid))
        {
            Debug.LogError($"[RoomJoiner] invalid room guid: {Guid}");
            yield break;
        }

        while (roomClient && !roomClient.JoinedRoom)
        {
            if (networkScene && networkScene.connectionCount > 0)
            {
                if (logJoinAttempts)
                {
                    Debug.Log($"[RoomJoiner] joining room guid={roomGuid} connections={networkScene.connectionCount}");
                }
                roomClient.Join(roomGuid);
            }
            else if (logJoinAttempts)
            {
                Debug.Log("[RoomJoiner] waiting for server connection before join");
            }

            yield return new WaitForSeconds(retrySeconds);
        }
    }
}
