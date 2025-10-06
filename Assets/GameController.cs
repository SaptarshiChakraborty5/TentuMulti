using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class GameController : MonoBehaviourPunCallbacks
{
    [Header("Panels")]
    public GameObject lobbyPanel;
    public GameObject gamePanel;

    [Header("Gameplay Refs")]
    public GameTimer gameTimer;
    public TentaizuGame tentaizuGame;

    [Header("Board Settings")]
    public int gridSize = 7;
    public int starCount = 10;

    // --- Turn-based state ---
    private int currentTurnPlayerId;
    private double turnStartTime;
    private float turnDuration = 30f;   // 30s per turn
    private int maxGuessesPerPlayer = 5;

    private Dictionary<int, HashSet<Vector2Int>> playerGuesses = new Dictionary<int, HashSet<Vector2Int>>();
    private Dictionary<int, double> playerTimeSpent = new Dictionary<int, double>();

    // ================== Room Join ==================
    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            GenerateBoard();
        }
    }

    private void GenerateBoard()
    {
        HashSet<Vector2Int> starPositions = new HashSet<Vector2Int>();
        System.Random rng = new System.Random();

        while (starPositions.Count < starCount)
        {
            int r = rng.Next(0, gridSize);
            int c = rng.Next(0, gridSize);
            starPositions.Add(new Vector2Int(r, c));
        }

        // Flatten positions into int[] for network
        int[] flatStars = starPositions
            .SelectMany(v => new int[] { v.x, v.y })
            .ToArray();

        // Send to all players
        photonView.RPC(nameof(SyncBoard), RpcTarget.AllBuffered, flatStars);
    }

    [PunRPC]
    private void SyncBoard(int[] flatStars)
    {
        HashSet<Vector2Int> starPositions = new HashSet<Vector2Int>();

        for (int i = 0; i < flatStars.Length; i += 2)
        {
            starPositions.Add(new Vector2Int(flatStars[i], flatStars[i + 1]));
        }

        // Call TentaizuGame to build the board using the SAME star data
        if (tentaizuGame != null)
            tentaizuGame.BuildBoard(starPositions);
    }

    // ================== Match Start ==================
    public void HostStartMatch()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int seed = Random.Range(int.MinValue, int.MaxValue);
        double startAt = PhotonNetwork.Time + 2.0; // 2s delay

        photonView.RPC(nameof(RPC_EnterGame), RpcTarget.AllBuffered, seed, startAt);
    }

    [PunRPC]
    private void RPC_EnterGame(int seed, double startAtNetworkTime)
    {
        lobbyPanel.SetActive(false);
        gamePanel.SetActive(true);

        if (tentaizuGame != null)
            tentaizuGame.StartWithSeed(seed);

        if (gameTimer != null)
            gameTimer.ConfigureAndStart(startAtNetworkTime);

        if (PhotonNetwork.IsMasterClient)
            BeginFirstTurn();
    }

    // ================== Turn Flow ==================
    private void BeginFirstTurn()
    {
        int first = Random.value > 0.5f
            ? PhotonNetwork.MasterClient.ActorNumber
            : PhotonNetwork.PlayerListOthers[0].ActorNumber;

        photonView.RPC(nameof(RPC_StartTurn), RpcTarget.All, first, PhotonNetwork.Time);
    }

    [PunRPC]
    private void RPC_StartTurn(int playerId, double startAtNetworkTime)
    {
        currentTurnPlayerId = playerId;
        turnStartTime = startAtNetworkTime;

        bool isMyTurn = (PhotonNetwork.LocalPlayer.ActorNumber == currentTurnPlayerId);
        tentaizuGame.SetInputEnabled(isMyTurn);

        if (gameTimer != null)
            gameTimer.StartTurnTimer(turnDuration, startAtNetworkTime);

        Debug.Log($"[Turn] Player {playerId} turn started");
    }

    private void Update()
    {
        if (PhotonNetwork.IsMasterClient && currentTurnPlayerId != 0)
        {
            double elapsed = PhotonNetwork.Time - turnStartTime;
            if (elapsed >= turnDuration)
                EndTurn();
        }
    }

    public void EndTurn()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Save player time
        double elapsed = PhotonNetwork.Time - turnStartTime;
        if (!playerTimeSpent.ContainsKey(currentTurnPlayerId))
            playerTimeSpent[currentTurnPlayerId] = 0;
        playerTimeSpent[currentTurnPlayerId] += elapsed;

        // Switch player
        int nextPlayer = PhotonNetwork.PlayerList
            .First(p => p.ActorNumber != currentTurnPlayerId).ActorNumber;

        photonView.RPC(nameof(RPC_StartTurn), RpcTarget.All, nextPlayer, PhotonNetwork.Time);
    }

    // ================== Guess Handling ==================
    public void SubmitGuess(Vector2Int cell)
    {
        // Send guess to all players (not just local)
        photonView.RPC(nameof(RPC_SubmitGuess), RpcTarget.All, cell.x, cell.y, currentTurnPlayerId);
    }

    [PunRPC]
    private void RPC_SubmitGuess(int x, int y, int playerId)
    {
        Vector2Int cell = new Vector2Int(x, y);

        // Track guesses per player
        if (!playerGuesses.ContainsKey(playerId))
            playerGuesses[playerId] = new HashSet<Vector2Int>();

        if (playerGuesses[playerId].Count >= maxGuessesPerPlayer)
            return; // already max guesses

        playerGuesses[playerId].Add(cell);

        // Update visuals for everyone
        tentaizuGame.ApplyGuess(cell);

        // End turn automatically if max guesses reached
        if (playerGuesses[playerId].Count == maxGuessesPerPlayer && PhotonNetwork.IsMasterClient)
            EndTurn();
    }

    // ================== Result ==================
    public void CheckWinner(HashSet<Vector2Int> solution)
    {
        int p1 = PhotonNetwork.MasterClient.ActorNumber;
        int p2 = PhotonNetwork.PlayerListOthers[0].ActorNumber;

        int score1 = playerGuesses.ContainsKey(p1) ? playerGuesses[p1].Count(c => solution.Contains(c)) : 0;
        int score2 = playerGuesses.ContainsKey(p2) ? playerGuesses[p2].Count(c => solution.Contains(c)) : 0;

        if (score1 != score2)
            Debug.Log(score1 > score2 ? "Player1 Wins!" : "Player2 Wins!");
        else
            Debug.Log(playerTimeSpent[p1] < playerTimeSpent[p2] ? "Player1 Wins (faster)!" : "Player2 Wins (faster)!");
    }
}
