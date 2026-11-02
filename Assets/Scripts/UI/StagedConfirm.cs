using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A button that asks before it does something that cannot be undone.
//
// Each tap moves its label on -- "Deletar conta", "Certeza?", "Absoluta?" -- and the last one runs the
// action. Left alone for a few seconds the label goes back to the start, so a stray tap in a pocket
// cannot creep towards deleting anything. One object per button; the page that owns it calls Tick from
// its own Update.
public class StagedConfirm
{
    private const float ForgetSeconds = 6f;
    private const float NoticeSeconds = 3f;

    private readonly Button button;
    private readonly string[] labels;
    private readonly Action onConfirmed;

    private int stage;
    private float resetAt;

    public StagedConfirm(Button button, string[] labels, Action onConfirmed)
    {
        this.button = button;
        this.labels = labels;
        this.onConfirmed = onConfirmed;

        if (button != null)
            button.onClick.AddListener(Tap);

        Reset();
    }

    // Back to the first label, waiting to be asked again.
    public void Reset()
    {
        stage = 0;
        resetAt = 0f;
        SetLabel(labels[0]);
    }

    // Puts a message on the button for a moment -- "Deletando...", "Sem conexão" -- then the first
    // label comes back on its own.
    public void ShowNotice(string message)
    {
        stage = 0;
        resetAt = Time.unscaledTime + NoticeSeconds;
        SetLabel(message);
    }

    public void Tick()
    {
        if (resetAt > 0f && Time.unscaledTime >= resetAt)
            Reset();
    }

    private void Tap()
    {
        stage++;
        resetAt = Time.unscaledTime + ForgetSeconds;

        if (stage < labels.Length)
        {
            SetLabel(labels[stage]);
            return;
        }

        stage = 0;
        resetAt = 0f;
        onConfirmed?.Invoke();
    }

    private void SetLabel(string text)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

        if (label != null)
            label.text = text;
    }
}
