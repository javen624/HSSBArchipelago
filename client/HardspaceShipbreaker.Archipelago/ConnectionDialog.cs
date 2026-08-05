using System;
using UnityEngine;

namespace HardspaceShipbreaker.Archipelago;

/// <summary>
/// IMGUI Archipelago connection panel. Persists fields via BepInEx config entries.
/// </summary>
internal sealed class ConnectionDialog
{
    private readonly Plugin _plugin;
    private string _server = "localhost";
    private string _port = "38281";
    private string _slot = "Player1";
    private string _password = "";
    private string _status = "";
    private bool _visible;
    private bool _cursorWasVisible;
    private bool _lockCursorWasLocked;
    private Rect _windowRect = new(80, 80, 420, 300);

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            if (_visible)
            {
                LoadFromConfig();
                _cursorWasVisible = Cursor.visible;
                _lockCursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                Cursor.visible = _cursorWasVisible;
                Cursor.lockState = _lockCursorWasLocked ? CursorLockMode.Locked : CursorLockMode.None;
            }
        }
    }

    public ConnectionDialog(Plugin plugin)
    {
        _plugin = plugin;
        LoadFromConfig();
    }

    public void LoadFromConfig()
    {
        _server = _plugin.Server.Value ?? "localhost";
        _port = _plugin.Port.Value.ToString();
        _slot = _plugin.Slot.Value ?? "Player1";
        _password = _plugin.Password.Value ?? "";
    }

    public void SetStatus(string message) => _status = message ?? "";

    public void Draw()
    {
        if (!_visible)
        {
            return;
        }

        _windowRect = GUI.Window(
            0x48534150, // "HSAP"
            _windowRect,
            DrawWindow,
            "Hardspace Shipbreaker — Archipelago");
    }

    private void DrawWindow(int id)
    {
        const float labelW = 90f;
        const float pad = 12f;
        var y = 28f;
        var fieldW = _windowRect.width - labelW - pad * 3;

        GUI.Label(new Rect(pad, y, labelW, 22), "Server");
        _server = GUI.TextField(new Rect(pad + labelW, y, fieldW, 22), _server ?? "");
        y += 28;

        GUI.Label(new Rect(pad, y, labelW, 22), "Port");
        _port = GUI.TextField(new Rect(pad + labelW, y, fieldW, 22), _port ?? "");
        y += 28;

        GUI.Label(new Rect(pad, y, labelW, 22), "Slot");
        _slot = GUI.TextField(new Rect(pad + labelW, y, fieldW, 22), _slot ?? "");
        y += 28;

        GUI.Label(new Rect(pad, y, labelW, 22), "Password");
        _password = GUI.PasswordField(new Rect(pad + labelW, y, fieldW, 22), _password ?? "", '*');
        y += 36;

        var client = _plugin.Client;
        var connected = client is { IsConnected: true };
        var connecting = client is { IsConnecting: true };

        GUI.enabled = !connecting;
        if (GUI.Button(new Rect(pad, y, 120, 28), connecting ? "Connecting…" : "Connect"))
        {
            TryConnect();
        }

        GUI.enabled = connected;
        if (GUI.Button(new Rect(pad + 130, y, 100, 28), "Disconnect"))
        {
            client?.Disconnect();
            _status = "Disconnected.";
        }

        GUI.enabled = true;
        if (GUI.Button(new Rect(pad + 240, y, 80, 28), "Close"))
        {
            Visible = false;
        }

        y += 36;
        var status = !string.IsNullOrEmpty(_status)
            ? _status
            : connected
                ? $"Connected as '{client?.SlotName}'. F6 = progress."
                : client is { IsReconnecting: true }
                    ? "Reconnecting… (AutoReconnect)"
                    : "Not connected. F6 progress · F7 this dialog.";
        GUI.Label(new Rect(pad, y, _windowRect.width - pad * 2, 48), status);

        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 24));
    }

    private void TryConnect()
    {
        if (!int.TryParse(_port.Trim(), out var port) || port <= 0 || port > 65535)
        {
            _status = "Port must be a number 1–65535.";
            return;
        }

        var server = (_server ?? "").Trim();
        var slot = (_slot ?? "").Trim();
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(slot))
        {
            _status = "Server and Slot are required.";
            return;
        }

        _plugin.Server.Value = server;
        _plugin.Port.Value = port;
        _plugin.Slot.Value = slot;
        _plugin.Password.Value = _password ?? "";
        _plugin.Config.Save();

        _status = $"Connecting to {server}:{port} as '{slot}'…";
        try
        {
            _plugin.Client?.Connect(server, port, slot, _password ?? "");
            if (_plugin.Client is { IsConnected: true })
            {
                _status = $"Connected as '{_plugin.Client.SlotName}'.";
                Visible = false;
            }
            else
            {
                _status = _plugin.Client?.LastConnectError ?? "Connection failed — see BepInEx log.";
            }
        }
        catch (Exception ex)
        {
            _status = $"Connect error: {ex.Message}";
        }
    }
}
