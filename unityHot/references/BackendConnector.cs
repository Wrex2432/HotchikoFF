using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if USE_NATIVE_WEBSOCKET
using NativeWebSocket;
#endif

public class BackendConnector : MonoBehaviour
{
    [SerializeField] private string serverUrl = "ws://localhost:3000";
    [SerializeField] private bool verboseLogs = true;

#if USE_NATIVE_WEBSOCKET
    private WebSocket ws;
#endif
    private bool connected;
    private string sessionCode = "";

    public event Action OnConnected;
    public event Action<string> OnDisconnected;
    public event Action<string> OnUnityCreated;
    public event Action<FacechinkoPlayerMsg> OnPlayerChanged;
    public event Action<FacechinkoPlayerMsg> OnPlayerLeft;
    public event Action<FacechinkoGameResultMsg> OnGameResult;
    public event Action<string> OnPaused;
    public event Action OnEnded;

    [Serializable]
    public class UnityCreateMsg
    {
        public string type = "unityCreate";
        public string gameType;
        public string location;
        public int teamCount;
        public int allowedNumberOfPlayers;
        public string requestedCode;
    }

    [Serializable]
    public class FacechinkoPlayerMsg
    {
        public string type;
        public FacechinkoPlayer player;
    }

    [Serializable]
    public class FacechinkoPlayer
    {
        public string uid;
        public string name;
        public int teamIndex;
    }

    [Serializable]
    public class FacechinkoGameResultMsg
    {
        public string type;
        public int winningTeamIndex;
        public int winningTeamId;
        public string mvpName;
    }

    public void SetServerUrl(string url) => serverUrl = url;
    public string GetSessionCode() => sessionCode;

    public void Connect()
    {
#if USE_NATIVE_WEBSOCKET
        if (ws != null)
        {
            try { ws.Close(); } catch { }
            ws = null;
        }

        ws = new WebSocket(serverUrl);
        ws.OnOpen += () =>
        {
            connected = true;
            if (verboseLogs) Debug.Log($"[Facechinko] Connected: {serverUrl}");
            OnConnected?.Invoke();
        };

        ws.OnClose += (e) =>
        {
            connected = false;
            var msg = $"closed_{e}";
            if (verboseLogs) Debug.LogWarning($"[Facechinko] Disconnected: {msg}");
            OnDisconnected?.Invoke(msg);
        };

        ws.OnError += (e) =>
        {
            connected = false;
            if (verboseLogs) Debug.LogError($"[Facechinko] WS Error: {e}");
            OnDisconnected?.Invoke(e);
        };

        ws.OnMessage += (bytes) =>
        {
            var json = Encoding.UTF8.GetString(bytes);
            HandleInbound(json);
        };

        ws.Connect();
#else
        connected = false;
        Debug.LogError("[Facechinko] BackendConnector needs NativeWebSocket. Add scripting define symbol USE_NATIVE_WEBSOCKET after installing dependency.");
        OnDisconnected?.Invoke("missing_native_websocket");
#endif
    }

    public async void Disconnect()
    {
#if USE_NATIVE_WEBSOCKET
        try
        {
            if (ws != null) await ws.Close();
        }
        catch { }
#endif
    }

    public void SendUnityCreate(UnityCreateMsg msg)
    {
        var json = $"{{\"type\":\"unityCreate\",\"gameType\":\"{Escape(msg.gameType)}\",\"location\":\"{Escape(msg.location)}\",\"teamCount\":{msg.teamCount},\"allowedNumberOfPlayers\":{msg.allowedNumberOfPlayers},\"requestedCode\":\"{Escape(msg.requestedCode)}\"}}";
        SendRaw(json);
    }

    public void SendPhase(string phase)
    {
        SendUnityMsgRaw($"{{\"kind\":\"phase\",\"phase\":\"{Escape(phase)}\"}}");
    }

    public void SendGameOver(int winningTeamIndex, string mvpUid)
    {
        SendUnityMsgRaw($"{{\"kind\":\"gameOver\",\"winningTeamIndex\":{winningTeamIndex},\"mvpUid\":\"{Escape(mvpUid)}\"}}");
    }

    public void SendUnityMsg(Dictionary<string, object> payload)
    {
        // Minimal helper for known payloads in this project.
        if (payload == null || payload.Count == 0) return;

        if (payload.TryGetValue("kind", out var kindObj) && (kindObj?.ToString() ?? "") == "powerReady")
        {
            var teamIndex = payload.TryGetValue("teamIndex", out var t) ? Convert.ToInt32(t) : 0;
            var powerId = payload.TryGetValue("powerId", out var p) ? p?.ToString() ?? "" : "";
            SendUnityMsgRaw($"{{\"kind\":\"powerReady\",\"teamIndex\":{teamIndex},\"powerId\":\"{Escape(powerId)}\"}}");
            return;
        }

        Debug.LogWarning("[Facechinko] SendUnityMsg received unsupported payload shape.");
    }

    private void SendUnityMsgRaw(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(sessionCode))
        {
            if (verboseLogs) Debug.LogWarning("[Facechinko] Tried to SendUnityMsg before sessionCode was set.");
            return;
        }

        var envelope = $"{{\"type\":\"unityMsg\",\"code\":\"{Escape(sessionCode)}\",\"payload\":{payloadJson}}}";
        SendRaw(envelope);
    }

    private async void SendRaw(string json)
    {
#if USE_NATIVE_WEBSOCKET
        if (!connected || ws == null) return;
        if (verboseLogs) Debug.Log($"[Facechinko] >> {json}");

        try
        {
            await ws.SendText(json);
        }
        catch (Exception e)
        {
            connected = false;
            Debug.LogError($"[Facechinko] SendText failed: {e.Message}");
            OnDisconnected?.Invoke(e.Message);
        }
#else
        Debug.LogWarning($"[Facechinko] (no websocket) skipped send: {json}");
#endif
    }

    private void HandleInbound(string json)
    {
        if (verboseLogs) Debug.Log($"[Facechinko] << {json}");

        var type = ExtractString(json, "type");
        if (string.IsNullOrWhiteSpace(type)) return;

        if (type == "unityCreated")
        {
            var ok = ExtractBool(json, "ok");
            if (ok)
            {
                sessionCode = ExtractString(json, "code") ?? "";
                OnUnityCreated?.Invoke(sessionCode);
            }
            else
            {
                var reason = ExtractString(json, "reason") ?? "unknown_unityCreated_failure";
                OnDisconnected?.Invoke($"unityCreated_failed_{reason}");
            }
            return;
        }

        if (type == "playerRegistered" || type == "playerJoined" || type == "playerResumed" || type == "playerLeft")
        {
            var msg = new FacechinkoPlayerMsg
            {
                type = type,
                player = new FacechinkoPlayer
                {
                    uid = ExtractString(json, "uid"),
                    name = ExtractString(json, "name"),
                    teamIndex = ExtractInt(json, "teamIndex"),
                }
            };

            if (!string.IsNullOrWhiteSpace(msg.player.uid))
            {
                if (type == "playerLeft") OnPlayerLeft?.Invoke(msg);
                else OnPlayerChanged?.Invoke(msg);
            }
            return;
        }

        if (type == "gameResult")
        {
            var result = new FacechinkoGameResultMsg
            {
                type = type,
                winningTeamIndex = ExtractInt(json, "winningTeamIndex"),
                winningTeamId = ExtractInt(json, "winningTeamId"),
                mvpName = ExtractString(json, "mvpName"),
            };
            OnGameResult?.Invoke(result);
            return;
        }

        if (type == "paused")
        {
            OnPaused?.Invoke(ExtractString(json, "reason") ?? "paused");
            return;
        }

        if (type == "ended")
        {
            OnEnded?.Invoke();
        }
    }

    private static string Escape(string input)
    {
        if (input == null) return "";
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ExtractString(string json, string key)
    {
        var needle = $"\"{key}\"";
        var i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return null;

        var colon = json.IndexOf(':', i + needle.Length);
        if (colon < 0) return null;

        var firstQuote = json.IndexOf('"', colon + 1);
        if (firstQuote < 0) return null;

        var secondQuote = json.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0) return null;

        return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    private static int ExtractInt(string json, string key)
    {
        var needle = $"\"{key}\"";
        var i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return 0;

        var colon = json.IndexOf(':', i + needle.Length);
        if (colon < 0) return 0;

        var j = colon + 1;
        while (j < json.Length && (json[j] == ' ' || json[j] == '"')) j++;

        var k = j;
        while (k < json.Length && (char.IsDigit(json[k]) || json[k] == '-')) k++;

        if (k <= j) return 0;
        return int.TryParse(json.Substring(j, k - j), out var n) ? n : 0;
    }

    private static bool ExtractBool(string json, string key)
    {
        var needle = $"\"{key}\"";
        var i = json.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return false;

        var colon = json.IndexOf(':', i + needle.Length);
        if (colon < 0) return false;

        var tail = json.Substring(colon + 1).TrimStart();
        if (tail.StartsWith("true", StringComparison.Ordinal)) return true;
        return false;
    }

    private void Update()
    {
#if USE_NATIVE_WEBSOCKET
        ws?.DispatchMessageQueue();
#endif
    }

    private void OnApplicationQuit() => Disconnect();
}
