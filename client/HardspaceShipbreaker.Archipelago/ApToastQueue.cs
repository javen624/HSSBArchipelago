using System;
using System.Collections.Generic;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// Always-on IMGUI toast stack for AP receive / send / location-check feedback.
/// </summary>
internal static class ApToastQueue
{
    private const float DurationSeconds = 6.5f;
    private const int MaxVisible = 3;
    private const float LineHeight = 42f;
    private const float Margin = 12f;
    private const float Width = 560f;

    private enum Kind
    {
        Info,
        Received,
        Checked
    }

    private sealed class Entry
    {
        public string Text = "";
        public Kind Kind;
        public float ExpiresAt;
    }

    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = new();
    private static GUIStyle? _labelStyle;
    private static GUIStyle? _boxStyle;
    private static Texture2D? _boxTex;

    public static void EnqueueReceived(string itemName, string fromPlayer)
    {
        var player = string.IsNullOrWhiteSpace(fromPlayer) ? "?" : fromPlayer;
        Enqueue(Kind.Received, $"Received {itemName} from {player}");
    }

    public static void EnqueueChecked(string locationName)
    {
        Enqueue(Kind.Checked, $"Checked: {locationName}");
    }

    public static void EnqueueSent(string itemName, string toPlayer)
    {
        var to = string.IsNullOrWhiteSpace(toPlayer) ? "?" : toPlayer;
        Enqueue(Kind.Checked, $"Sent {itemName} to {to}");
    }

    public static void EnqueueInfo(string message)
    {
        Enqueue(Kind.Info, message);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
        }
    }

    public static void Draw()
    {
        float now;
        try
        {
            now = Time.unscaledTime;
        }
        catch
        {
            return;
        }

        Entry[] snapshot;
        lock (Gate)
        {
            Entries.RemoveAll(e => e.ExpiresAt <= now);
            while (Entries.Count > MaxVisible)
            {
                Entries.RemoveAt(0);
            }

            snapshot = Entries.ToArray();
        }

        if (snapshot.Length == 0)
        {
            return;
        }

        EnsureStyles();
        var prev = GUI.color;
        var x = Screen.width - Width - Margin;
        var y = Margin;

        for (var i = 0; i < snapshot.Length; i++)
        {
            var e = snapshot[i];
            var remaining = e.ExpiresAt - now;
            var alpha = remaining < 0.75f ? Mathf.Clamp01(remaining / 0.75f) : 1f;
            var color = ColorFor(e.Kind);
            color.a *= alpha;
            GUI.color = color;

            var rect = new Rect(x, y + i * (LineHeight + 4f), Width, LineHeight);
            GUI.Box(rect, GUIContent.none, _boxStyle);
            GUI.Label(rect, "  " + e.Text, _labelStyle);
        }

        GUI.color = prev;
    }

    private static void Enqueue(Kind kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        float now;
        try
        {
            now = Time.unscaledTime;
        }
        catch
        {
            now = 0f;
        }

        lock (Gate)
        {
            Entries.Add(new Entry
            {
                Text = text,
                Kind = kind,
                ExpiresAt = now + DurationSeconds
            });
            while (Entries.Count > MaxVisible * 2)
            {
                Entries.RemoveAt(0);
            }
        }
    }

    private static Color ColorFor(Kind kind) =>
        kind switch
        {
            Kind.Received => new Color(0.45f, 0.85f, 0.95f, 0.92f),
            Kind.Checked => new Color(0.95f, 0.75f, 0.35f, 0.92f),
            _ => new Color(0.85f, 0.85f, 0.85f, 0.9f)
        };

    private static void EnsureStyles()
    {
        if (_labelStyle != null && _boxStyle != null)
        {
            return;
        }

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        _labelStyle.normal.textColor = Color.white;

        _boxTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _boxTex.SetPixel(0, 0, new Color(0.05f, 0.07f, 0.1f, 0.75f));
        _boxTex.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.normal.background = _boxTex;
        _boxStyle.border = new RectOffset(0, 0, 0, 0);
    }
}
