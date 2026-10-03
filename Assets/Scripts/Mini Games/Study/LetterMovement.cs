using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Handles the motion of a single letter from its spawn point to the assigned box.
/// Also listens for collisions with the eraser (if you use the eraser logic).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LetterMovement : MonoBehaviour {
    private FivePositionsGameManager manager;
    private TextMeshPro textMesh;
    private int boxIndex;
    private char letterChar;
    private Vector3 targetPos;
    private float moveSpeed;
    private AudioSource audioSource;
    public AudioClip eraserClip;
    private float hangTimeRemaining;
    private bool hasStartedFalling;

    [Header("Drop Warning Shake")]
    public float shakeAmplitude = 0.08f;
    public float shakeFrequency = 18f;

    private Vector3 spawnPos;
    private float initialHangTime;
    private float shakeSeed;
    private bool isWaitingForTurn;

    [Header("Eraser Hide")]
    public float eraseHideDuration = 0.4f; // total time for the bottom-to-top erase wipe before the letter is destroyed
    private bool isErasing;

    [Header("Miss Effect (wrong letter reaches the box)")]
    public Color missColor = Color.red;
    public float missBounceDistance = 0.3f;
    public float missBounceUpDuration = 0.12f;
    public float missBounceSettleDuration = 0.1f;
    public float missFadeDuration = 0.3f;
    private bool isMissed;

    [Header("Fall Boost (Down Input)")]
    public float fallBoostMultiplier = 4f;
    private bool isBoosted;

    /// <summary>Time left before this letter starts falling; used by the manager to find the next one to drop.</summary>
    public float HangTimeRemaining => hangTimeRemaining;

    private void Awake() {
        textMesh = GetComponentInChildren<TextMeshPro>();
    }

    /// <summary>
    /// Called right after instantiating a Letter prefab,
    /// sets up everything needed for it to move and know its manager.
    /// </summary>
    public void Initialize(
        FivePositionsGameManager manager,
        int boxIndex,
        char letterChar,
        Vector3 targetPos,
        float moveSpeed,
        float preDropHangTime
    ) {
        this.manager = manager;
        this.boxIndex = boxIndex;
        this.letterChar = letterChar;
        this.targetPos = targetPos;
        this.moveSpeed = moveSpeed;
        spawnPos = transform.position;
        isWaitingForTurn = false;
        shakeSeed = Random.Range(0f, 100f);
        initialHangTime = Mathf.Max(0f, preDropHangTime);
        hangTimeRemaining = initialHangTime;
        hasStartedFalling = (hangTimeRemaining <= 0f);
        if (!hasStartedFalling) manager.RegisterHangingLetter(this);
    }

    /// <summary>
    /// Spawns this letter parked and idle above its column (part of the upfront jumble
    /// reveal) — visible, showing its letter, but not counting down or falling until
    /// the manager later calls Activate() when it's this column's turn.
    /// </summary>
    public void InitializeIdle(
        FivePositionsGameManager manager,
        int boxIndex,
        char letterChar,
        Vector3 targetPos,
        float moveSpeed
    ) {
        this.manager = manager;
        this.boxIndex = boxIndex;
        this.letterChar = letterChar;
        this.targetPos = targetPos;
        this.moveSpeed = moveSpeed;
        spawnPos = transform.position;
        isWaitingForTurn = true;
        hasStartedFalling = false;
        hangTimeRemaining = 0f;
    }

    /// <summary>Called when it becomes this (already-idle) letter's turn to drop.</summary>
    public void Activate(float hangTime)
    {
        isWaitingForTurn = false;
        shakeSeed = Random.Range(0f, 100f);
        initialHangTime = Mathf.Max(0f, hangTime);
        hangTimeRemaining = initialHangTime;
        hasStartedFalling = (hangTimeRemaining <= 0f);
        if (!hasStartedFalling) manager.RegisterHangingLetter(this);
    }

    private void Update() {
        if (isWaitingForTurn) return;
        if (isMissed) return; // MissEffectRoutine owns transform/visuals for the rest of this letter's life

        if (!hasStartedFalling)
        {
            hangTimeRemaining -= Time.deltaTime;

            // Only the single letter that's about to drop next shakes; others hold still.
            if (manager.IsNextToFall(this))
            {
                float progress = initialHangTime > 0f ? 1f - Mathf.Clamp01(hangTimeRemaining / initialHangTime) : 1f;
                float shakeX = Mathf.Sin((Time.time + shakeSeed) * shakeFrequency) * shakeAmplitude * progress;
                transform.position = spawnPos + new Vector3(shakeX, 0f, 0f);
            }
            else
            {
                transform.position = spawnPos;
            }

            if (hangTimeRemaining <= 0f)
            {
                hasStartedFalling = true;
                manager.UnregisterHangingLetter(this);
                transform.position = spawnPos;
            }
            return;
        }

        // Move letter towards the box (boosted while the down input is held)
        float speed = moveSpeed * (isBoosted ? fallBoostMultiplier : 1f);
        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPos,
            speed * Time.deltaTime
        );

        // Being erased: keep falling (so it visually slides behind the eraser) but never report arrival.
        if (isErasing) return;

        // If close enough to the box, inform the manager
        if (Vector3.Distance(transform.position, targetPos) < 0.01f) {
            manager.OnLetterArrived(boxIndex, letterChar, gameObject);
        }
    }

    /// <summary>
    /// If the eraser hits this letter, destroy it immediately.
    /// (Assumes your pencil/eraser flips have colliders with tag "EraserCollider" or similar).
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("EraserCollider")) {
            Erase();
        }
    }

    /// <summary>
    /// Properly erases this letter: spawns this column's replacement preview immediately,
    /// then wipes the glyph out from the bottom up (via its own mesh vertex alpha, so it
    /// doesn't depend on the eraser sprite's opacity or on render/sorting order) before
    /// destroying the object. Both this script's own trigger check and EraserController's
    /// route through here, so a column never sits blank waiting for its next letter.
    /// </summary>
    public void Erase() {
        if (isErasing) return;
        isErasing = true;
        if (manager != null) manager.SpawnReplacementLetter(boxIndex, this);
//        audioSource.PlayOneShot(eraserClip);
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (textMesh != null) StartCoroutine(EraseWipeRoutine());
        else Destroy(gameObject);
    }

    /// <summary>
    /// Fades the glyph's own mesh vertices out in two halves: bottom pair first, then the
    /// top pair, so the letter visibly erases from the bottom of the glyph up to the top
    /// regardless of what's drawn in front of or behind it.
    /// </summary>
    private IEnumerator EraseWipeRoutine() {
        textMesh.ForceMeshUpdate();
        TMP_TextInfo textInfo = textMesh.textInfo;
        if (textInfo.characterCount == 0) {
            Destroy(gameObject);
            yield break;
        }

        TMP_CharacterInfo charInfo = textInfo.characterInfo[0];
        int matIndex = charInfo.materialReferenceIndex;
        int vertIndex = charInfo.vertexIndex;
        Color32[] colors = textInfo.meshInfo[matIndex].colors32;
        // TMP per-character vertex order: 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
        byte bottomStartAlpha = colors[vertIndex + 0].a;
        byte topStartAlpha = colors[vertIndex + 1].a;

        float halfDuration = eraseHideDuration * 0.5f;

        float elapsed = 0f;
        while (elapsed < halfDuration) {
            elapsed += Time.deltaTime;
            byte a = (byte)(bottomStartAlpha * (1f - Mathf.Clamp01(elapsed / halfDuration)));
            SetVertexAlpha(colors, vertIndex + 0, a);
            SetVertexAlpha(colors, vertIndex + 3, a);
            textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            yield return null;
        }
        SetVertexAlpha(colors, vertIndex + 0, 0);
        SetVertexAlpha(colors, vertIndex + 3, 0);

        elapsed = 0f;
        while (elapsed < halfDuration) {
            elapsed += Time.deltaTime;
            byte a = (byte)(topStartAlpha * (1f - Mathf.Clamp01(elapsed / halfDuration)));
            SetVertexAlpha(colors, vertIndex + 1, a);
            SetVertexAlpha(colors, vertIndex + 2, a);
            textMesh.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            yield return null;
        }

        Destroy(gameObject);
    }

    private static void SetVertexAlpha(Color32[] colors, int index, byte alpha) {
        Color32 c = colors[index];
        c.a = alpha;
        colors[index] = c;
    }

    /// <summary>
    /// Called by the manager when this letter reached its box with the wrong answer and
    /// wasn't erased in time: turns red, bounces back away from the box, then fades out
    /// and destroys itself. The manager still handles penalty/strike logic and spawning
    /// this column's replacement; this just owns the miss letter's own death animation.
    /// </summary>
    public void PlayMissEffect() {
        if (isErasing || isMissed) return;
        isMissed = true;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (textMesh != null) StartCoroutine(MissEffectRoutine());
        else Destroy(gameObject);
    }

    private IEnumerator MissEffectRoutine() {
        textMesh.color = missColor;

        Vector3 startPos = transform.position;
        Vector3 upPos = startPos + Vector3.up * missBounceDistance;
        Vector3 settlePos = startPos + Vector3.up * (missBounceDistance * 0.4f);

        yield return MoveOverTime(startPos, upPos, missBounceUpDuration);
        yield return MoveOverTime(upPos, settlePos, missBounceSettleDuration);

        Color baseColor = textMesh.color;
        float elapsed = 0f;
        while (elapsed < missFadeDuration) {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / missFadeDuration));
            textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator MoveOverTime(Vector3 from, Vector3 to, float duration) {
        if (duration <= 0f) {
            transform.position = to;
            yield break;
        }
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        transform.position = to;
    }

    /// <summary>Called by the manager while the down input is held, to speed up this letter's fall.</summary>
    public void SetBoosted(bool boosted) {
        isBoosted = boosted;
    }

    /// <summary>
    /// Called by the manager on submit input: resolves this letter immediately, as if it had
    /// just landed in its box, regardless of whether it's still hanging or mid-fall. Snapping
    /// to the box position first keeps DestroyLettersOnSameX's x-check and the miss-effect's
    /// bounce-back both looking correct, same as a natural arrival. If the letter is currently
    /// sitting on the eraser, that teleport would otherwise skip right past it, so erase instead
    /// of locking in/missing.
    /// </summary>
    public void ResolveNow() {
        if (isErasing || isMissed) return;
        if (IsCaughtByEraser()) {
            // Redirect the fall toward the eraser's position (instead of the box) and boost
            // speed, so Update()'s existing fall movement visually rushes it down into the
            // eraser while Erase()'s wipe fade plays, rather than fading out in place.
            EraserController eraser = FindAnyObjectByType<EraserController>();
            if (eraser != null)
                targetPos = new Vector3(eraser.transform.position.x, eraser.transform.position.y, transform.position.z);
            isBoosted = true;
            Erase();
            return;
        }
        transform.position = targetPos;
        if (manager != null) manager.OnLetterArrived(boxIndex, letterChar, gameObject);
    }

    private bool IsCaughtByEraser() {
        EraserController eraser = FindAnyObjectByType<EraserController>();
        return eraser != null && eraser.CurrentColumnIndex == boxIndex;
    }

    /// <summary>Safety net: the manager destroys letters from several code paths, so always clean up the registry here.</summary>
    private void OnDestroy() {
        if (manager != null) manager.UnregisterHangingLetter(this);
    }
}
