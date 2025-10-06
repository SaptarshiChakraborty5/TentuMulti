using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;

public class TentaizuGame : MonoBehaviourPunCallbacks
{
    [Header("Board Settings")]
    public int gridSize = 7;
    public int starCount = 10;

    [Tooltip("Prefab named 'Cell' with an Image + Button on root and a child TMP named 'ClueText'")]
    public GameObject cellPrefab;

    [Tooltip("The RectTransform with the GridLayoutGroup (BoardGrid)")]
    public Transform boardParent;

    [Header("UI Elements (optional)")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI moveCounterText;

    [Header("UI Buttons")]
    public Button returnToLobbyButton; // lobby button
    public TextMeshProUGUI winnerAnnouncementText; // NEW: winner text

    [Header("UI Panels")]
    public GameObject lobbyPanel;   // assign LobbyPanel in Inspector
    public GameObject gamePanel;    // assign GamePanel in Inspector

    [Header("Colors & Icons")]
    public Color clueCellColor = new Color(0.9f, 0.3f, 0.5f);
    public Color emptyCellColor = new Color(0.9f, 0.3f, 0.5f);
    public Color markedCellColor = new Color(0.6f, 0.1f, 0.2f);
    public Sprite starSprite;
    public Sprite normalSprite;

    // --- runtime state ---
    private char[,] board;
    private HashSet<Vector2Int> solution;
    private HashSet<Vector2Int> guesses;
    private Stack<(string, Vector2Int)> history;
    private Stack<(string, Vector2Int)> redoHistory;

    private Button[,] cellButtons;
    private TextMeshProUGUI[,] cellTexts;
    private Image[,] cellImages;

    private float elapsedTime;
    private int moveCount;

    private bool isPlayer1Turn = true; // NEW: local turn tracker
    private bool gameEnded = false;    // NEW: end flag

    // ================== Unity ==================
    private void Awake()
    {
        if (cellPrefab == null) Debug.LogError("[Tentaizu] Assign Cell Prefab.");
        if (boardParent == null) Debug.LogError("[Tentaizu] Assign BoardGrid (boardParent).");

        var grid = boardParent != null ? boardParent.GetComponent<GridLayoutGroup>() : null;
        if (grid == null) Debug.LogError("[Tentaizu] BoardGrid must have a GridLayoutGroup.");

        if (cellPrefab != null)
        {
            if (cellPrefab.GetComponent<Button>() == null) Debug.LogError("[Tentaizu] Cell prefab needs a Button component on root.");
            if (cellPrefab.GetComponent<Image>() == null) Debug.LogError("[Tentaizu] Cell prefab needs an Image on root.");
            var tmp = cellPrefab.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null) Debug.LogError("[Tentaizu] Cell prefab needs a child TMP named 'ClueText' (or any TMP child).");
        }
    }

    private void Start()
    {
        // Hook up ReturnToLobby button
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.onClick.RemoveAllListeners();
            returnToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        // Hide announcement text at start
        if (winnerAnnouncementText != null)
        {
            winnerAnnouncementText.gameObject.SetActive(false);
        }

        // Do nothing else. GameController will call BuildBoard().
    }

    private void Update()
    {
        elapsedTime += Time.deltaTime;
        if (timerText != null)
        {
            int m = Mathf.FloorToInt(elapsedTime / 60f);
            int s = Mathf.FloorToInt(elapsedTime % 60f);
            timerText.text = $"{m:00}:{s:00}";
        }
    }

    // ================== Public Entry Points ==================
    public void StartWithSeed(int seed)
    {
        Random.InitState(seed);
        StartGame();
    }

    // ================== Lobby / Exit ==================
    public void ReturnToLobby()
    {
        // Switch panels instead of loading a new scene
        if (lobbyPanel != null && gamePanel != null)
        {
            lobbyPanel.SetActive(true);
            gamePanel.SetActive(false);
        }
        else
        {
            // Fallback: if panels not set, use scene loading
            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.LoadLevel("LobbyScene");
            }
            else
            {
                PhotonNetwork.LeaveRoom();
                SceneManager.LoadScene("LobbyScene");
            }
        }

        // If you're in a Photon room and not host, still leave the room
        if (!PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LeaveRoom();
        }

        Debug.Log("Returned to Lobby.");
    }

    // ================== Core Game Flow ==================
    private void StartGame()
    {
        guesses = new HashSet<Vector2Int>();
        history = new Stack<(string, Vector2Int)>();
        redoHistory = new Stack<(string, Vector2Int)>();
        elapsedTime = 0f;
        moveCount = 0;
        if (moveCounterText) moveCounterText.text = "0";

        CreateBoardUI();
        UpdateBoardUI();
    }

    private void CreateBoardUI()
    {
        foreach (Transform child in boardParent) Destroy(child.gameObject);

        cellButtons = new Button[gridSize, gridSize];
        cellTexts = new TextMeshProUGUI[gridSize, gridSize];
        cellImages = new Image[gridSize, gridSize];

        for (int r = 0; r < gridSize; r++)
        {
            for (int c = 0; c < gridSize; c++)
            {
                var cellGO = Instantiate(cellPrefab, boardParent);
                var btn = cellGO.GetComponent<Button>();
                var img = cellGO.GetComponent<Image>();
                var txt = cellGO.transform.Find("ClueText")?.GetComponent<TextMeshProUGUI>();
                if (txt == null) txt = cellGO.GetComponentInChildren<TextMeshProUGUI>(true);

                int rr = r, cc = c;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnCellClicked_ForwardToGameController(rr, cc));

                cellButtons[r, c] = btn;
                cellImages[r, c] = img;
                cellTexts[r, c] = txt;

                txt.text = (board != null && board[r, c] != ' ') ? board[r, c].ToString() : "";
                img.sprite = normalSprite;
                img.color = (board != null && board[r, c] == ' ') ? emptyCellColor : clueCellColor;
            }
        }
    }

    // ================== Multiplayer Input ==================
    private void OnCellClicked_ForwardToGameController(int r, int c)
    {
        var gc = FindObjectOfType<GameController>();
        if (gc != null)
            gc.SubmitGuess(new Vector2Int(r, c));

        // NEW: track turn locally
        if (!gameEnded)
        {
            isPlayer1Turn = !isPlayer1Turn;
        }
    }

    // ================== Undo/Redo ==================
    public void Undo()
    {
        if (history == null || history.Count == 0) return;

        var (action, cell) = history.Pop();
        if (redoHistory == null) redoHistory = new Stack<(string, Vector2Int)>();
        redoHistory.Push((action, cell));

        if (action == "mark")
        {
            if (guesses != null) guesses.Remove(cell);
            moveCount = Mathf.Max(0, moveCount - 1);
        }
        else if (action == "unmark")
        {
            if (guesses == null) guesses = new HashSet<Vector2Int>();
            guesses.Add(cell);
            moveCount++;
        }

        if (moveCounterText) moveCounterText.text = moveCount.ToString();
        UpdateBoardUI();
    }

    public void Redo()
    {
        if (redoHistory == null || redoHistory.Count == 0) return;

        var (action, cell) = redoHistory.Pop();
        if (history == null) history = new Stack<(string, Vector2Int)>();
        history.Push((action, cell));

        if (action == "mark")
        {
            if (guesses == null) guesses = new HashSet<Vector2Int>();
            guesses.Add(cell);
            moveCount++;
        }
        else if (action == "unmark")
        {
            if (guesses != null) guesses.Remove(cell);
            moveCount = Mathf.Max(0, moveCount - 1);
        }

        if (moveCounterText) moveCounterText.text = moveCount.ToString();
        UpdateBoardUI();
    }

    public void ResetGuesses()
    {
        guesses.Clear();
        history.Clear();
        redoHistory.Clear();
        moveCount = 0;
        if (moveCounterText) moveCounterText.text = "0";
        UpdateBoardUI();
    }

    public void ShowSolution()
    {
        guesses = new HashSet<Vector2Int>(solution);
        UpdateBoardUI();
    }

    // ================== Turn Handling ==================
    public void SetInputEnabled(bool enabled)
    {
        if (cellButtons == null) return;
        for (int r = 0; r < gridSize; r++)
            for (int c = 0; c < gridSize; c++)
                cellButtons[r, c].interactable = enabled && board[r, c] == ' ';
    }

    // Called by GameController RPC so everyone applies the same guess
    public void ApplyGuess(Vector2Int cell)
    {
        if (board == null) return;
        if (board[cell.x, cell.y] != ' ') return;
        if (guesses == null) guesses = new HashSet<Vector2Int>();
        if (guesses.Contains(cell)) return;

        guesses.Add(cell);

        if (history == null) history = new Stack<(string, Vector2Int)>();
        if (redoHistory == null) redoHistory = new Stack<(string, Vector2Int)>();
        history.Push(("mark", cell));
        redoHistory.Clear();

        moveCount++;
        if (moveCounterText) moveCounterText.text = moveCount.ToString();

        UpdateBoardUI();
        CheckWin(); // NEW: check win after applying guess
    }

    private void CheckWin()
    {
        if (guesses == null) return;
        if (guesses.Count == starCount)
        {
            if (guesses.SetEquals(solution))
            {
                Debug.Log("You found all stars!");
                EndGame(isPlayer1Turn ? "Player 1" : "Player 2");
            }
            else
            {
                Debug.Log("Not all correct.");
            }
        }
    }

    private void EndGame(string winner)
    {
        gameEnded = true;

        // Debug console
        Debug.Log($"{winner} wins the game!");

        // In-game announcement
        if (winnerAnnouncementText != null)
        {
            winnerAnnouncementText.gameObject.SetActive(true);
            winnerAnnouncementText.text = $"{winner} Wins!";
        }
    }

    private void UpdateBoardUI()
    {
        if (board == null) return;
        if (cellTexts == null || cellImages == null) return;

        for (int r = 0; r < gridSize; r++)
        {
            for (int c = 0; c < gridSize; c++)
            {
                var cell = new Vector2Int(r, c);
                var txt = cellTexts[r, c];
                var img = cellImages[r, c];

                if (guesses != null && guesses.Contains(cell))
                {
                    img.sprite = starSprite != null ? starSprite : img.sprite;
                    img.color = markedCellColor;
                    txt.text = "";
                }
                else
                {
                    img.sprite = normalSprite != null ? normalSprite : img.sprite;
                    bool isClue = (board[r, c] != ' ');
                    img.color = isClue ? clueCellColor : emptyCellColor;
                    txt.text = isClue ? board[r, c].ToString() : "";
                }
            }
        }
    }

    // ================== Multiplayer Board Builder ==================
    public void BuildBoard(HashSet<Vector2Int> starPositions)
    {
        solution = new HashSet<Vector2Int>(starPositions);

        board = new char[gridSize, gridSize];
        for (int r = 0; r < gridSize; r++)
        {
            for (int c = 0; c < gridSize; c++)
            {
                var pos = new Vector2Int(r, c);
                if (solution.Contains(pos))
                {
                    board[r, c] = ' ';
                }
                else
                {
                    int count = GetNeighbors(r, c).Count(n => solution.Contains(n));
                    board[r, c] = (count > 0) ? count.ToString()[0] : ' ';
                }
            }
        }

        guesses = new HashSet<Vector2Int>();
        history = new Stack<(string, Vector2Int)>();
        redoHistory = new Stack<(string, Vector2Int)>();
        moveCount = 0;
        if (moveCounterText) moveCounterText.text = "0";

        foreach (Transform child in boardParent)
            Destroy(child.gameObject);

        cellButtons = new Button[gridSize, gridSize];
        cellTexts = new TextMeshProUGUI[gridSize, gridSize];
        cellImages = new Image[gridSize, gridSize];

        for (int r = 0; r < gridSize; r++)
        {
            for (int c = 0; c < gridSize; c++)
            {
                GameObject cell = Instantiate(cellPrefab, boardParent);
                InitCell(cell, r, c);
            }
        }

        UpdateBoardUI();
    }

    private void InitCell(GameObject cellGO, int r, int c)
    {
        if (cellGO == null) return;

        var btn = cellGO.GetComponent<Button>();
        var img = cellGO.GetComponent<Image>();
        var txt = cellGO.transform.Find("ClueText")?.GetComponent<TextMeshProUGUI>();
        if (txt == null) txt = cellGO.GetComponentInChildren<TextMeshProUGUI>(true);

        cellButtons[r, c] = btn;
        cellImages[r, c] = img;
        cellTexts[r, c] = txt;

        txt.text = (board[r, c] != ' ') ? board[r, c].ToString() : "";
        img.sprite = normalSprite;
        img.color = (board[r, c] == ' ') ? emptyCellColor : clueCellColor;

        int rr = r, cc = c;
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCellClicked_ForwardToGameController(rr, cc));
        }
    }

    private List<Vector2Int> GetNeighbors(int r, int c)
    {
        var list = new List<Vector2Int>(8);
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr >= 0 && nr < gridSize && nc >= 0 && nc < gridSize)
                    list.Add(new Vector2Int(nr, nc));
            }
        return list;
    }

    [ContextMenu("Rebuild Board")]
    private void RebuildBoard()
    {
        StartGame();
    }
}
