using UnityEngine;
using TMPro;
using VNEngine;

public class HomeClockController : MonoBehaviour
{
    [Header("Display")]
    public TextMeshProUGUI timeText;

    private const int MORNING_START   = 8  * 60;   // 480  = 8:00 AM
    private const int AFTERNOON_START = 13 * 60;   // 780  = 1:00 PM
    private const int MIDNIGHT        = 24 * 60;   // 1440 = midnight (display cap)

    private const float REAL_SECONDS_PER_GAME_MINUTE = 10f;

    private float _elapsedGameMinutes;

    private void OnEnable()
    {
        _elapsedGameMinutes = 0f;
        UpdateDisplay(GetCurrentMinute());
    }

    private void Update()
    {
        _elapsedGameMinutes += Time.deltaTime / REAL_SECONDS_PER_GAME_MINUTE;
        UpdateDisplay(GetCurrentMinute());
    }

    private int GetCurrentMinute()
    {
        // DayPhase 0 = Morning (show 8 AM onward), 1 = Afternoon (show 1 PM onward)
        int dayPhase    = Mathf.RoundToInt(StatsManager.Get_Numbered_Stat("DayPhase"));
        int periodStart = dayPhase == 1 ? AFTERNOON_START : MORNING_START;
        return periodStart + Mathf.FloorToInt(_elapsedGameMinutes);
    }

    private void UpdateDisplay(int totalMinutes)
    {
        totalMinutes = Mathf.Min(totalMinutes, MIDNIGHT - 1);
        int h        = (totalMinutes / 60) % 24;
        int m        = totalMinutes % 60;
        string amPm  = h < 12 ? "AM" : "PM";
        int displayH = h == 0 ? 12 : (h > 12 ? h - 12 : h);
        if (timeText != null)
            timeText.text = $"{displayH}:{m:D2} {amPm}";
    }
}
