using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using VNEngine;

[System.Serializable]
public class QuestionAnswerPair
{
    public string question;
    public string definition;
    public string answer; // Must be 5 letters
    public bool alreadyUsed = false;
    public bool isPrimary = false;
}

public class FivePositionsGameManager : MonoBehaviour
{
    public enum SpawnMode { Sequential, Random }
    public GameMode currentMode;
    public bool useTimer;
    public SpawnMode spawnMode;    private int lastSpawnedColumn = -1;
    public int maxWrongGuesses = 3;

    public StudyQuestionLoader questionLoader;

    
    private QuestionAnswerPair _currentQuestion;
[Header("Scene References")]
    public RectTransform[] boxPositions = new RectTransform[5];
    public TextMeshProUGUI[] boxLetterDisplays = new TextMeshProUGUI[5];
    public TextMeshProUGUI targetDefinitionText;
    public TextMeshProUGUI countdownText; // "3-2-1" countdown text
    public TextMeshProUGUI scoreText;
    
    private ConversationManager conversationManager;
    [Header("Prefabs/Assets")]
    public GameObject letterPrefab;
    public AudioClip correctClip;
    public AudioClip incorrectClip;
    public GameObject boxSpritePrefab;
    public GameObject eraserPrefab;
    [Header("Offsets")]
    public float spawnYOffset = 100f; // how far above each box the letter should spawn
    public float boxYOffset = 0f;         // Optional adjustment (e.g., -0.5f if needed)
    public float eraserYOffset = 2f;      // How far above the first box to place the eraser
    [Header("Spawn Settings")]
    public float minSpawnInterval = 1f;
    public float maxSpawnInterval = 3f;
    [Range(0f, 1f)] public float chanceOfCorrectLetter = 0.3f;
    public float letterSpeed = 2f;
    [Header("No-Timer Rules")]
    public int strikesPerWord = 3;
    public int maxWordAttempts = 5;

    private int strikesThisWord = 0;
    private int wordsAttempted = 0;

    private string targetWord = "";
    private char[] targetLetters = new char[5];
    private bool[] boxFilled = new bool[5];
    private string alphabet = "abcdefghijklmnopqrstuvwxyz";

    private int score = 0;
    [Header("Letter Timing")]
    public float preDropHangTime = 0.35f;

    [Header("Timer Settings")]
    public GameObject timers;
    public float gameDuration = 180f;       // Total game time in seconds
    public TextMeshProUGUI timerText;      // Displays remaining time
    
    public TextMeshProUGUI penaltyText;    // Briefly shows "-0:20" or similar
    public float penaltyTime = 5f;        // Seconds to remove on incorrect answer
    public GameObject studyGameParent;       // Panel to show when time runs out
    [SerializeField] private GameObject gameStuff;
    public TextMeshProUGUI finalScoreText; // Display final score on game over panel
    private GameObject eraser;
    public float timeLeft;
    private bool gameIsOver = false;
    private Coroutine spawnRoutine;
    public int wrongGuessCount = 0;
    // This bool will pause the timer when true
    private bool isTimerPaused = false;
    public List<int> activeColumns = new List<int>();
    private List<GameObject> boxVisuals = new List<GameObject>();
    public ChallengeProfile pendingChallengeProfile;
    public ConversationManager pendingEndConversation;

    public void Initialize()
    {
        SetMode(currentMode);
        SpawnVisuals();
    }

    public void LaunchExam()
    {
        Initialize();

        if (pendingChallengeProfile != null)
            ConfigureChallenge(pendingChallengeProfile);

        StartGame();
    }


    private void SpawnVisuals()
    {
        for (int i = 0; i < boxPositions.Length; i++)
        {
            RectTransform box = boxPositions[i];
            if (box == null) continue;

            // ✅ This respects the entire transform hierarchy, including y = -3
            Vector3 worldBoxCenter = box.position;

            // Add world-space vertical offset if needed
            Vector3 worldPos = worldBoxCenter + new Vector3(0, boxYOffset, 0);
            worldPos.z = 0;

            GameObject visual = Instantiate(boxSpritePrefab, worldPos, Quaternion.identity);
            boxVisuals.Add(visual);
            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Foreground";
                sr.sortingOrder = 100;
            }

        }



        // Spawn the eraser above the first column
// Spawn the eraser above the first column
        Transform firstBox = boxPositions[0];
        if (firstBox != null)
        {
            Vector3 eraserPos = firstBox.position + new Vector3(0, eraserYOffset, 0);
            eraser = Instantiate(eraserPrefab, eraserPos, Quaternion.identity);

            // Set sprite rendering layer
            SpriteRenderer sr = eraser.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Foreground";
                sr.sortingOrder = 200;
            }

            // Connect the eraser to the box positions
            EraserController eraserController = eraser.GetComponent<EraserController>();
            if (eraserController != null)
            {
                eraserController.boxPositions = boxPositions;
            }
            else
            {
                Debug.LogWarning("Eraser prefab is missing EraserController.");
            }
        }
    }

    private IEnumerator DelayedGameStart()
    {
        yield return new WaitForEndOfFrame();
        StartGame();
    }
    
    private Vector3 GetSpawnPosAboveBox(int index)
    {
        if (boxPositions == null || index < 0 || index >= boxPositions.Length)
        {
            Debug.LogError("Invalid box index.");
            return Vector3.zero;
        }

        RectTransform rect = boxPositions[index];
        if (rect == null)
        {
            Debug.LogError("Box is not a RectTransform.");
            return Vector3.zero;
        }

        // World-space center of the box, including parent offset
        Vector3 worldBoxCenter = rect.position;

        // Offset vertically in world units
        Vector3 spawnPos = worldBoxCenter + new Vector3(0, spawnYOffset, 0);
        spawnPos.z = 0;

        return spawnPos;
    }




    public void StartGame()
    {
        // Initialize score UI
        UpdateScoreUI();

        // Hide penalty text and game-over panel at start
        if (penaltyText != null) penaltyText.gameObject.SetActive(false);
        if (gameStuff != null) gameStuff.SetActive(true);

        // Initialize the timer but don�t let it tick yet
        timeLeft = gameDuration;
        UpdateTimerUI();
        isTimerPaused = true;
        gameIsOver = false;
        // Start the timer coroutine right away 
        StartCoroutine(GameTimerCoroutine());
        if (questionLoader != null)
            questionLoader.currentMode = currentMode;
        questionLoader.LoadQuestionsForMode();
        if (currentMode == GameMode.Exam && questionLoader.currentQuestions.Count > 0)
            maxWordAttempts = questionLoader.currentQuestions.Count;
        // Start the first countdown
        StartCoroutine(CountdownCoroutine());
        
    }
    /// <summary>
    /// Main game timer. It only decrements timeLeft if isTimerPaused is false.
    /// </summary>
    private IEnumerator GameTimerCoroutine() {
        while (timeLeft > 0 && !gameIsOver) {
            yield return null; // Wait one frame
            if (useTimer)
            {
                if (!isTimerPaused)
                {
                    timeLeft -= Time.deltaTime;
                    UpdateTimerUI();

                    if (timeLeft <= 0 && !gameIsOver)
                    {
                        timeLeft = 0;
                        UpdateTimerUI();
                        StartCoroutine(EndGame());

                    }
                }
            }
        }
    }

    /// <summary>
    /// Shows a short "3-2-1" countdown, then unpauses the timer and spawns letters.
    /// </summary>
    private IEnumerator CountdownCoroutine(bool skipCountdown = false) {
        // Start or reset the round�s target word
        StartNewRound();
        if (!skipCountdown)
        {
            
            if (countdownText != null) {
                countdownText.gameObject.SetActive(true);

                countdownText.text = "3";
                yield return new WaitForSeconds(1f);

                countdownText.text = "2";
                yield return new WaitForSeconds(1f);

                countdownText.text = "1";
                yield return new WaitForSeconds(1f);

                countdownText.gameObject.SetActive(false);
            }
        }

        // Now that the countdown is done, unpause the timer and spawn letters
        if (!gameIsOver) {
            isTimerPaused = false;
            if (spawnRoutine != null)
            {
                Debug.LogWarning("Spawn routine already running — not starting another.");
                yield break;
            }
            spawnRoutine = StartCoroutine(SpawnLettersRoutine());
        }
    }

    /// <summary>
    /// Spawns letters at random intervals until boxes are filled or game ends.
    /// </summary>
    private IEnumerator SpawnLettersRoutine() {
        while (!AllBoxesFilled() && !gameIsOver)
        {
            List<int> spawnableIndices = new List<int>();
            for (int i = 0; i < boxPositions.Length; i++)
            {
                if (!boxFilled[i] && !LetterInColumn(i))
                    spawnableIndices.Add(i);
            }

            // ✅ Nothing available? Stop trying to spawn and wait for update loop
            if (spawnableIndices.Count == 0)
            {
                // Check if we're just waiting for remaining letters to arrive
                bool waitingForDelivery = false;
                for (int i = 0; i < boxFilled.Length; i++)
                {
                    if (!boxFilled[i] && LetterInColumn(i))
                    {
                        waitingForDelivery = true;
                        break;
                    }
                }

                if (!waitingForDelivery)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue; // instead of yield break
                }

                yield return null;
                continue;
            }

            int selectedIndex;

            if (spawnMode == SpawnMode.Sequential)
            {
                int attempts = 0;
                do
                {
                    lastSpawnedColumn = (lastSpawnedColumn + 1) % boxPositions.Length;
                    selectedIndex = lastSpawnedColumn;
                    attempts++;
                }
                while ((!spawnableIndices.Contains(selectedIndex)) && attempts <= boxPositions.Length);
            }
            else // Random
            {
                selectedIndex = spawnableIndices[Random.Range(0, spawnableIndices.Count)];
            }

            // Decide whether to spawn a correct letter or random letter
            char letterToSpawn = Random.value < chanceOfCorrectLetter
                ? targetLetters[selectedIndex]
                : alphabet[Random.Range(0, alphabet.Length)];

            // Instantiate the new letter
            Vector3 spawnPos = GetSpawnPosAboveBox(selectedIndex);
            GameObject newLetter = Instantiate(letterPrefab, spawnPos, Quaternion.identity);
            RegisterActiveColumn(selectedIndex);
            // Set letter text
            TextMeshPro textComp = newLetter.GetComponentInChildren<TextMeshPro>();
            if (textComp != null) {
                textComp.text = letterToSpawn.ToString();
            }

            // Initialize movement
            LetterMovement letterMovement = newLetter.GetComponent<LetterMovement>();
            letterMovement.Initialize(
                this,
                selectedIndex,
                letterToSpawn,
                boxPositions[selectedIndex].position,
                letterSpeed,
                preDropHangTime
            );

            // Wait before next spawn
            float waitTime = Random.Range(minSpawnInterval, maxSpawnInterval);
            yield return new WaitForSeconds(waitTime);
        }
    }
    

    public void RegisterActiveColumn(int index)
    {
        if (!activeColumns.Contains(index))
            activeColumns.Add(index);
    }

    public void UnregisterActiveColumn(int index)
    {
        activeColumns.Remove(index);
    }

    private bool LetterInColumn(int index)
    {
        return activeColumns.Contains(index);
    }


    private void StartNewRound()
    {
        wordsAttempted++;
        strikesThisWord = 0;

        if (maxWordAttempts > 0 && wordsAttempted > maxWordAttempts)
        {
            StartCoroutine(EndGame());
            return;
        }

        QuestionAnswerPair question = SelectRandomQuestion();
        _currentQuestion = question;
        if (question != null)
        {
            targetWord = question.answer.ToLower(); // Ensure lowercase for consistency
            if (targetDefinitionText != null)
            {
                targetDefinitionText.text = questionLoader.GetDisplayPrompt(question, questionLoader.useDefinitions);
            }
            else
            {
                targetWord = "error";
                targetDefinitionText.text = "No valid 5-letter questions!";
            }

            // Reset box UI
            for (int i = 0; i < 5; i++)
            {
                targetLetters[i] = targetWord[i];
                boxFilled[i] = false;
                if (boxLetterDisplays[i] != null)
                {
                    boxLetterDisplays[i].text = " ";
                }
            }
        }
        else
        {
            Debug.LogWarning($"Invalid question selected. Length is {question.answer.Length}");
        }
    }

    public void ConfigureChallenge(ChallengeProfile profile)
    {
        timeLeft = profile.timerDuration;
        minSpawnInterval = profile.minSpawnInterval;
        maxSpawnInterval = profile.maxSpawnInterval;
        chanceOfCorrectLetter = profile.chanceOfCorrectLetter;
        preDropHangTime = profile.preDropHangTime;
        strikesPerWord = profile.strikesPerWord;
        maxWordAttempts = profile.maxWordAttempts;
        timers.SetActive(true);

        // Decide whether we're using definitions or questions
        bool useDefinitions = profile.promptType == ChallengeProfile.PromptType.Definitions;
        questionLoader.useDefinitions = useDefinitions;
        questionLoader.LoadQuestionsForMode(); // fallback
    }

    private QuestionAnswerPair SelectRandomQuestion()
    {
        if (questionLoader == null || questionLoader.currentQuestions == null || questionLoader.currentQuestions.Count == 0)
        {
            Debug.LogWarning("No loaded questions available. Returning default.");
            return new QuestionAnswerPair { question = "Missing data", answer = "error" };
        }

        return questionLoader.GetRandomQuestion();
    }

// Use this instead of clearLeftoverLetters() if you want to keep eraser/box visuals alive:
    private void ClearOnlyLetters()
    {
        var leftoverLetters = GameObject.FindGameObjectsWithTag("Letter");
        foreach (var letter in leftoverLetters)
            Destroy(letter);

        activeColumns.Clear();
    }
    private IEnumerator FailCurrentWordAndAdvance()
    {
        if (questionLoader != null && _currentQuestion != null) questionLoader.MarkFail(_currentQuestion.answer);
        strikesThisWord = 0;

        // Clear the current attempt
        ClearOnlyLetters();
        for (int i = 0; i < 5; i++)
        {
            boxFilled[i] = false;
            if (boxLetterDisplays[i] != null)
                boxLetterDisplays[i].text = " ";
        }

        // Next word attempt
        if (wordsAttempted >= maxWordAttempts)
        {
            StartCoroutine(EndGame());
            yield break;
        }

        // Optional: small beat so the fail "lands"
        yield return new WaitForSeconds(0.25f);

        // Restart round (you can skip countdown if you prefer)
        spawnRoutine = null;
        StartCoroutine(CountdownCoroutine(skipCountdown: true));
    }

    private bool AllBoxesFilled() {
        int filledCount = 0;
        foreach (bool filled in boxFilled) {
            if (!filled) return false;
            filledCount++;
        }
        return true;
    }

    /// <summary>
    /// Called by LetterMovement when a letter reaches its box.
    /// </summary>
    public void OnLetterArrived(int boxIndex, char arrivedLetter, GameObject letterObj) { 
        if (gameIsOver) { 
            if (letterObj != null) Destroy(letterObj); 
            return;
        }
        
        if (boxIndex < 0 || boxIndex >= boxFilled.Length) { 
            if (letterObj != null) Destroy(letterObj); 
            return;
        }
        if (boxFilled[boxIndex]) { 
            // Already filled with a correct letter
            if (letterObj != null) Destroy(letterObj); 
            return;
        }

        // Check if correct letter
        if (arrivedLetter == targetLetters[boxIndex]) {
            boxFilled[boxIndex] = true;
            if (boxLetterDisplays[boxIndex] != null) {
                boxLetterDisplays[boxIndex].text = arrivedLetter.ToString();
            }
            DestroyLettersOnSameX(letterObj.transform.position.x);
        } else {
            // Incorrect letter
            // Two different rule sets:
            // 1) Timer mode -> apply time penalty (and optional feedback)
            // 2) No-timer (Group/Exam) -> strikes-per-word; fail word => clear attempt + new word
            if (useTimer) { 
                // Apply penalty
                timeLeft -= penaltyTime; 
                if (timeLeft < 0) timeLeft = 0; 
                UpdateTimerUI();
                // Show penalty text briefly
                if (penaltyText != null) { 
                    penaltyText.gameObject.SetActive(true); 
                    penaltyText.text = string.Format("-0:{0:00}", (int)penaltyTime);
                    StartCoroutine(HidePenaltyText());
                }
            }
            else { 
                // Wrong letter wasn't erased (it made it to the bottom) -> strike
                strikesThisWord++; 
                // Optional: you can reuse penaltyText for strike feedback if desired
                 if (penaltyText != null) { penaltyText.gameObject.SetActive(true); penaltyText.text = $"Strike {strikesThisWord}/{strikesPerWord}"; StartCoroutine(HidePenaltyText()); }
                 if (strikesPerWord > 0 && strikesThisWord >= strikesPerWord) { 
                     // Clean up this arriving letter & column tracking before advancing
                     UnregisterActiveColumn(boxIndex); 
                     if (letterObj != null) Destroy(letterObj);
                     // Ensure we don't keep spawning into the old round while we reset
                     if (spawnRoutine != null) { 
                         StopCoroutine(spawnRoutine); 
                         spawnRoutine = null;
                     }
                    
                     // Fail the current word and move to the next attempt
                     StartCoroutine(FailCurrentWordAndAdvance()); 
                     return;
                 }
            }
            
        }
        UnregisterActiveColumn(boxIndex);
        if (letterObj != null) Destroy(letterObj);

        // If all boxes are filled, increase score & start next round
        if (AllBoxesFilled()) {
            if (questionLoader != null && _currentQuestion != null) questionLoader.MarkSuccess(_currentQuestion.answer);
            score++;
            UpdateScoreUI();
            StartCoroutine(RestartGameRoutine());
        }

        // If timer is out, end game
        if (timeLeft <= 0 && !gameIsOver && useTimer) {
            StartCoroutine(EndGame());

        }
    }

    /// <summary>
    /// Hides penalty text after a short delay.
    /// </summary>
    private IEnumerator HidePenaltyText() {
        yield return new WaitForSeconds(1f);
        if (penaltyText != null) {
            penaltyText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Destroy all letters at a given x-position (to remove duplicates).
    /// </summary>
    private void DestroyLettersOnSameX(float xPosition) {
        GameObject[] allLetters = GameObject.FindGameObjectsWithTag("Letter");
        foreach (GameObject letter in allLetters) {
            if (Mathf.Abs(letter.transform.position.x - xPosition) < 0.1f) {
                Destroy(letter);
            }
        }
    }

    /// <summary>
    /// After a short delay, pause the timer, clear boxes, do a new countdown, then unpause.
    /// </summary>
    private IEnumerator RestartGameRoutine() {
        //half the time between spawns
        minSpawnInterval = Mathf.Max(minSpawnInterval * 0.9f, 0.3f); // limit how fast it gets
        maxSpawnInterval = Mathf.Max(maxSpawnInterval * 0.9f, 1f);
        // Wait briefly so the player can see the filled boxes
        yield return new WaitForSeconds(2f);

        if (!gameIsOver) {
            // Pause timer during the countdown
            isTimerPaused = true;
            spawnRoutine = null;
            // Clear boxes
            for (int i = 0; i < 5; i++) {
                if (boxLetterDisplays[i] != null) {
                    boxLetterDisplays[i].text = " ";
                }
                boxFilled[i] = false;
            }
            // Start the timer coroutine right away
            StartCoroutine(GameTimerCoroutine());
            // Run another "3-2-1" countdown, which will unpause the timer again
            StartCoroutine(CountdownCoroutine(false));
            
            
            
        }
    }

    /// <summary>
    /// Updates the UI score label.
    /// </summary>
    private void UpdateScoreUI() {
        if (scoreText != null) {
            scoreText.text = score.ToString();
        }
    }

    /// <summary>
    /// Ends the game, stops coroutines, and shows the Game Over panel.
    /// </summary>
    private IEnumerator EndGame()
    {
        gameIsOver = true;
        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);
        spawnRoutine = null;
        clearLeftoverLetters();

        if (currentMode == GameMode.Exam)
        {
            StatsManager.Set_Numbered_Stat("ExamRawScore", score);
        }
        else
        {
            float existing = StatsManager.Get_Numbered_Stat("StudyGameScore");
            if (score > existing)
                StatsManager.Set_Numbered_Stat("StudyGameScore", score);

            float studyRaw  = StatsManager.Get_Numbered_Stat("StudyGameScore");
            float studyNorm = studyRaw >= 5 ? 4f : studyRaw >= 4 ? 3f : studyRaw >= 3 ? 2f : studyRaw >= 1 ? 1f : 0f;
            float mid       = StatsManager.Get_Numbered_Stat("MidtermScore");
            float fin       = StatsManager.Get_Numbered_Stat("FinalScore");
            float grades    = fin > 0f ? (studyNorm + mid + fin) / 3f
                            : mid > 0f ? (studyNorm + mid) / 2f
                            : studyNorm;
            StatsManager.Set_Numbered_Stat("Grades", Mathf.Clamp(grades, 0f, 4f));
        }
        // Mark exam event complete and clear the stale exam ID so non-exam study
        // sessions don't accidentally re-complete a future exam.
        string examId = StatsManager.Get_String_Stat("CurrentExamId");
        if (!string.IsNullOrEmpty(examId))
        {
            GameEvents.MarkCustomEventCompleted(examId, true);
            StatsManager.Set_String_Stat("CurrentExamId", "");
        }

        // Branch by context
        if (VNSceneManager.scene_manager != null)
        {
            VNSceneManager.scene_manager.Show_UI(true);
            switch (currentMode)
            {
                case GameMode.Exam:
                    VNSceneManager.scene_manager.Start_Conversation(pendingEndConversation);
                    break;
                case GameMode.Group:
                    VNSceneManager.scene_manager.Start_Conversation(conversationManager);
                    break;
                case GameMode.Solo:
                    HomeCutsceneController.Instance?.OnStudyComplete();
                    yield break;
            }
        }
        else
        {
            // Fallback: no VN manager present (Home.unity Solo context)
            HomeCutsceneController.Instance?.OnStudyComplete();
            yield break;
        }

        if (questionLoader != null && questionLoader.currentQuestions != null)
        {
            foreach (var q in questionLoader.currentQuestions)
                q.alreadyUsed = false;
        }

        studyGameParent.SetActive(false);
        pendingChallengeProfile = null;
        pendingEndConversation  = null;

    }
    
    /// <summary>
    /// Destroys any leftover letters still on the screen.
    /// </summary>
    private void clearLeftoverLetters() {
        var leftoverLetters = GameObject.FindGameObjectsWithTag("Letter");
        foreach (var letter in leftoverLetters) {
            Destroy(letter);
        }

        foreach (var box in boxVisuals)
        {
            Destroy(box);
        }
        Destroy(eraser);
    }

    private void UpdateTimerUI() {
        if (timerText != null) {
            int minutes = Mathf.FloorToInt(timeLeft / 60f);
            int seconds = Mathf.FloorToInt(timeLeft % 60f);
            timerText.text = string.Format("{0:0}:{1:00}", minutes, seconds);
        }
    }
    public void SetMode(GameMode mode)
    {
        currentMode = mode;
        if (questionLoader != null)
            questionLoader.currentMode = mode;

        switch (mode)
        {
            case GameMode.Solo:
                spawnMode = SpawnMode.Random;
                useTimer = true;
                if (questionLoader != null) questionLoader.useDefinitions = false;
                break;

            case GameMode.Group:
                GroupStudyManager groupStudyManager = FindObjectOfType<GroupStudyManager>();
                conversationManager = groupStudyManager.conversationManager;
                spawnMode = SpawnMode.Sequential;
                useTimer = false;
                if (questionLoader != null) questionLoader.useDefinitions = true;
                break;

            case GameMode.Exam:
                spawnMode = SpawnMode.Random;
                useTimer = false; // or true, depending on your exam design
                break;
        }

        timerText.gameObject.SetActive(useTimer);
    }

}
