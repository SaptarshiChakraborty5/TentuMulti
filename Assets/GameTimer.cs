using UnityEngine;
using Photon.Pun;

public class GameTimer : MonoBehaviourPunCallbacks
{
    private bool running = false;
    private double startTime;
    private float duration = 300f; // default 5 minutes

    public TMPro.TextMeshProUGUI timerText;

    public void ConfigureAndStart(double startAtNetworkTime)
    {
        startTime = startAtNetworkTime;
        running = true;
    }

    // ✅ New method added
    public void StartTurnTimer(float duration, double startAtNetworkTime)
    {
        this.duration = duration;
        this.startTime = startAtNetworkTime;
        this.running = true;
    }

    void Update()
    {
        if (!running) return;

        double timePassed = PhotonNetwork.Time - startTime;
        double timeLeft = duration - timePassed;

        if (timeLeft <= 0)
        {
            running = false;
            timeLeft = 0;
            Debug.Log("⏰ Time’s up!");
        }

        if (timerText != null)
        {
            int m = Mathf.FloorToInt((float)timeLeft / 60f);
            int s = Mathf.FloorToInt((float)timeLeft % 60f);
            timerText.text = $"{m:00}:{s:00}";
        }
    }
}
