using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("UI")]
    public GameObject lobbyPanel;
    public GameObject gamePanel;
    public Button createButton;
    public Button joinButton;
    public Button startButton;          // host-only
    public TMP_Text statusText;

    [Header("Name/Room Input")]
    public TMP_InputField nameInputField;   // <--- assign this in Inspector (TextMeshPro InputField)
    public TMP_InputField roomInputField;   // optional: room input

    [Header("Refs")]
    public GameController gameController;

    [Header("Room")]
    public string roomName = "TentaizuRoom";
    public byte maxPlayers = 2;

    void Start()
    {
        lobbyPanel.SetActive(true);
        gamePanel.SetActive(false);

        if (startButton) startButton.gameObject.SetActive(false);
        if (createButton) createButton.interactable = false;
        if (joinButton) joinButton.interactable = false;

        UpdateStatus("Connecting to Photon...");
        PhotonNetwork.ConnectUsingSettings();
    }

    void UpdateStatus(string msg)
    {
        if (statusText)
            statusText.text = msg;
        Debug.Log("[Lobby] " + msg);
    }

    // ---------- NAME helper ----------
    // call this from HostGame/JoinGame (or via the InputField OnEndEdit event)
    public void SetPlayerName()
    {
        if (nameInputField == null)
        {
            Debug.LogWarning("[LobbyManager] nameInputField is not assigned in Inspector!");
            // still set a fallback name
            PhotonNetwork.NickName = "Player" + Random.Range(1000, 9999);
            UpdateStatus("Player name set to fallback: " + PhotonNetwork.NickName);
            return;
        }

        string typed = nameInputField.text?.Trim();
        if (string.IsNullOrEmpty(typed))
        {
            // fallback auto-generated name
            PhotonNetwork.NickName = "Player" + Random.Range(1000, 9999);
            UpdateStatus("No name typed → fallback name: " + PhotonNetwork.NickName);
        }
        else
        {
            PhotonNetwork.NickName = typed;
            UpdateStatus("Player name set to: " + PhotonNetwork.NickName);
        }

        Debug.Log("[LobbyManager] PhotonNetwork.NickName = " + PhotonNetwork.NickName);
    }

    // ---------- Photon callbacks ----------
    public override void OnConnectedToMaster()
    {
        UpdateStatus("Connected! Joining lobby...");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        UpdateStatus("In lobby. You can Create or Join.");
        if (createButton) createButton.interactable = true;
        if (joinButton) joinButton.interactable = true;
    }

    // ---------- Host / Join ----------
    public void HostGame()
    {
        // set name before creating room
        SetPlayerName();

        string targetRoom = (roomInputField != null && !string.IsNullOrEmpty(roomInputField.text.Trim()))
            ? roomInputField.text.Trim()
            : roomName;

        RoomOptions opt = new RoomOptions { MaxPlayers = maxPlayers };
        PhotonNetwork.CreateRoom(targetRoom, opt);
        UpdateStatus("Creating room: " + targetRoom);
    }

    public void JoinGame()
    {
        // set name before joining room
        SetPlayerName();

        string targetRoom = (roomInputField != null && !string.IsNullOrEmpty(roomInputField.text.Trim()))
            ? roomInputField.text.Trim()
            : roomName;

        PhotonNetwork.JoinRoom(targetRoom);
        UpdateStatus("Joining room: " + targetRoom);
    }

    public override void OnJoinedRoom()
    {
        UpdateStatus($"Joined room ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})\nYou are: {PhotonNetwork.NickName}");
        // show start only to host when ready
        RefreshStartButton();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateStatus($"{newPlayer.NickName} joined! ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        RefreshStartButton();
    }

    void RefreshStartButton()
    {
        bool canStart = PhotonNetwork.IsMasterClient &&
                        PhotonNetwork.CurrentRoom != null &&
                        PhotonNetwork.CurrentRoom.PlayerCount == PhotonNetwork.CurrentRoom.MaxPlayers;

        if (startButton) startButton.gameObject.SetActive(canStart);
    }

    // host presses start
    public void OnStartButtonPressed()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (PhotonNetwork.CurrentRoom == null) return;
        if (PhotonNetwork.CurrentRoom.PlayerCount != PhotonNetwork.CurrentRoom.MaxPlayers) return;

        UpdateStatus("Starting match…");
        if (gameController != null) gameController.HostStartMatch();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        UpdateStatus("Create failed: " + message);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        UpdateStatus("Join failed: " + message);
    }
}
