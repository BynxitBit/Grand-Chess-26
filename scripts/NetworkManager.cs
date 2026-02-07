using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace GrandChess26;

public enum ConnectionPhase
{
    Idle,
    CreatingOffer,
    WaitingForAnswer,
    CreatingAnswer,
    WaitingForConnection,
    Connected
}

public partial class NetworkManager : Node
{
    public const int MaxClients = 1; // 1v1 only

    private WebRtcMultiplayerPeer _rtcMultiplayer;
    private WebRtcPeerConnection _rtcPeer;
    private ConnectionPhase _phase = ConnectionPhase.Idle;
    private bool _webrtcAvailable = true;

    // ICE gathering
    private string _localSdpType;
    private string _localSdp;
    private readonly List<Dictionary<string, object>> _iceCandidates = new();
    private double _iceGatherTimer = 0;
    private const double IceGatherTimeout = 5.0;
    private bool _iceGatheringStarted = false;
    private bool _firstCandidateReceived = false;

    // Player assignments
    public long HostPeerId { get; private set; } = 1;
    public long ClientPeerId { get; private set; } = 0;
    public bool IsHost => _rtcMultiplayer != null && Multiplayer.IsServer();
    public bool IsClient => _rtcMultiplayer != null && !Multiplayer.IsServer() && Multiplayer.HasMultiplayerPeer();
    public bool IsOnline => Multiplayer.HasMultiplayerPeer();
    public long MyPeerId => Multiplayer.GetUniqueId();

    // Host is always White, Client is always Black
    public bool AmIWhite => IsHost;

    public ConnectionPhase Phase => _phase;

    [Signal]
    public delegate void ConnectionSucceededEventHandler();

    [Signal]
    public delegate void ConnectionFailedEventHandler(string reason);

    [Signal]
    public delegate void PeerConnectedEventHandler(long peerId);

    [Signal]
    public delegate void PeerDisconnectedEventHandler(long peerId);

    [Signal]
    public delegate void GameStartReceivedEventHandler(int setupMode, string boardState);

    [Signal]
    public delegate void MoveRequestReceivedEventHandler(int fromX, int fromY, int toX, int toY, int promotionType);

    [Signal]
    public delegate void MoveConfirmedEventHandler(int fromX, int fromY, int toX, int toY, int promotionType);

    [Signal]
    public delegate void GameEndedEventHandler(int result);

    [Signal]
    public delegate void SessionCodeReadyEventHandler(string code);

    [Signal]
    public delegate void ConnectionPhaseChangedEventHandler(int phase);

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;

        // Check if WebRTC native extension is loaded
        var testPeer = new WebRtcPeerConnection();
        var err = testPeer.Initialize();
        if (err != Error.Ok)
        {
            GD.PrintErr("WebRTC native extension not available. Install addons/webrtc/");
            _webrtcAvailable = false;
        }
        testPeer.Close();
    }

    public bool IsWebRtcAvailable => _webrtcAvailable;

    private Godot.Collections.Dictionary GetIceConfig()
    {
        return new Godot.Collections.Dictionary
        {
            ["iceServers"] = new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    ["urls"] = new Godot.Collections.Array
                    {
                        "stun:stun.l.google.com:19302",
                        "stun:stun1.l.google.com:19302"
                    }
                }
            }
        };
    }

    // === Host Flow ===

    public void HostGame()
    {
        if (!_webrtcAvailable)
        {
            EmitSignal(SignalName.ConnectionFailed, "WebRTC extension not available");
            return;
        }

        CleanupRtc();

        _rtcMultiplayer = new WebRtcMultiplayerPeer();
        _rtcMultiplayer.CreateServer();
        Multiplayer.MultiplayerPeer = _rtcMultiplayer;
        HostPeerId = 1;

        // Create the peer connection for the joiner (peer ID 2)
        _rtcPeer = new WebRtcPeerConnection();
        _rtcPeer.Initialize(GetIceConfig());

        _rtcPeer.SessionDescriptionCreated += OnSessionDescriptionCreated;
        _rtcPeer.IceCandidateCreated += OnIceCandidateCreated;

        _rtcMultiplayer.AddPeer(_rtcPeer, 2);

        // Reset ICE state
        _iceCandidates.Clear();
        _localSdpType = null;
        _localSdp = null;
        _iceGatherTimer = 0;
        _iceGatheringStarted = false;
        _firstCandidateReceived = false;

        SetPhase(ConnectionPhase.CreatingOffer);

        // Create offer - signals will fire during _Process polling
        _rtcPeer.CreateOffer();

        GD.Print("Host: Creating WebRTC offer...");
    }

    // === Join Flow ===

    public void JoinGame(string sessionCode)
    {
        if (!_webrtcAvailable)
        {
            EmitSignal(SignalName.ConnectionFailed, "WebRTC extension not available");
            return;
        }

        // Decode host's session code
        SessionData hostData;
        try
        {
            hostData = DecodeSessionCode(sessionCode);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Failed to decode session code: {e.Message}");
            EmitSignal(SignalName.ConnectionFailed, "Invalid session code");
            return;
        }

        if (hostData.Type != "offer")
        {
            EmitSignal(SignalName.ConnectionFailed, "Invalid session code (not a host code)");
            return;
        }

        CleanupRtc();

        _rtcMultiplayer = new WebRtcMultiplayerPeer();
        _rtcMultiplayer.CreateClient(2);
        Multiplayer.MultiplayerPeer = _rtcMultiplayer;

        // Create the peer connection for the host (peer ID 1)
        _rtcPeer = new WebRtcPeerConnection();
        _rtcPeer.Initialize(GetIceConfig());

        _rtcPeer.SessionDescriptionCreated += OnSessionDescriptionCreated;
        _rtcPeer.IceCandidateCreated += OnIceCandidateCreated;

        _rtcMultiplayer.AddPeer(_rtcPeer, 1);

        // Reset ICE state
        _iceCandidates.Clear();
        _localSdpType = null;
        _localSdp = null;
        _iceGatherTimer = 0;
        _iceGatheringStarted = false;
        _firstCandidateReceived = false;

        SetPhase(ConnectionPhase.CreatingAnswer);

        // Set the host's offer as remote description - this triggers answer generation
        _rtcPeer.SetRemoteDescription(hostData.Type, hostData.Sdp);

        // Add host's ICE candidates
        foreach (var candidate in hostData.Candidates)
        {
            _rtcPeer.AddIceCandidate(candidate.Media, candidate.Index, candidate.Name);
        }

        GD.Print("Join: Processing host offer, generating answer...");
    }

    // === Host applies joiner's answer ===

    public void ApplyAnswerCode(string sessionCode)
    {
        if (_phase != ConnectionPhase.WaitingForAnswer)
        {
            GD.PrintErr("Not in WaitingForAnswer phase");
            return;
        }

        SessionData answerData;
        try
        {
            answerData = DecodeSessionCode(sessionCode);
        }
        catch (Exception e)
        {
            GD.PrintErr($"Failed to decode answer code: {e.Message}");
            EmitSignal(SignalName.ConnectionFailed, "Invalid answer code");
            return;
        }

        if (answerData.Type != "answer")
        {
            EmitSignal(SignalName.ConnectionFailed, "Invalid code (not a response code)");
            return;
        }

        // Set the joiner's answer as remote description
        _rtcPeer.SetRemoteDescription(answerData.Type, answerData.Sdp);

        // Add joiner's ICE candidates
        foreach (var candidate in answerData.Candidates)
        {
            _rtcPeer.AddIceCandidate(candidate.Media, candidate.Index, candidate.Name);
        }

        SetPhase(ConnectionPhase.WaitingForConnection);
        GD.Print("Host: Applied answer, waiting for connection...");
    }

    public void Disconnect()
    {
        CleanupRtc();
        Multiplayer.MultiplayerPeer = null;
        ClientPeerId = 0;
        SetPhase(ConnectionPhase.Idle);
        GD.Print("Disconnected from network");
    }

    private void CleanupRtc()
    {
        if (_rtcPeer != null)
        {
            _rtcPeer.SessionDescriptionCreated -= OnSessionDescriptionCreated;
            _rtcPeer.IceCandidateCreated -= OnIceCandidateCreated;
            _rtcPeer.Close();
            _rtcPeer = null;
        }
        if (_rtcMultiplayer != null)
        {
            _rtcMultiplayer = null;
        }
    }

    private void SetPhase(ConnectionPhase phase)
    {
        _phase = phase;
        EmitSignal(SignalName.ConnectionPhaseChanged, (int)phase);
    }

    // === Process: poll WebRTC and handle ICE gathering timeout ===

    public override void _Process(double delta)
    {
        if (_phase == ConnectionPhase.Idle || _phase == ConnectionPhase.Connected)
            return;

        // Poll is handled by WebRtcMultiplayerPeer when set as multiplayer peer,
        // but we poll explicitly during signaling to ensure signals fire
        _rtcPeer?.Poll();

        // ICE gathering timeout
        if (_iceGatheringStarted && _firstCandidateReceived)
        {
            _iceGatherTimer += delta;
            if (_iceGatherTimer >= IceGatherTimeout)
            {
                OnIceGatheringComplete();
            }
        }

        // Check gathering state
        if (_iceGatheringStarted && _rtcPeer != null)
        {
            var gatherState = _rtcPeer.GetGatheringState();
            if (gatherState == WebRtcPeerConnection.GatheringState.Complete)
            {
                OnIceGatheringComplete();
            }
        }

        // Check for connection during WaitingForConnection phase
        if (_phase == ConnectionPhase.WaitingForConnection && _rtcPeer != null)
        {
            var connState = _rtcPeer.GetConnectionState();
            if (connState == WebRtcPeerConnection.ConnectionState.Connected)
            {
                SetPhase(ConnectionPhase.Connected);
                GD.Print("WebRTC connection established!");
            }
            else if (connState == WebRtcPeerConnection.ConnectionState.Failed)
            {
                GD.PrintErr("WebRTC connection failed");
                Disconnect();
                EmitSignal(SignalName.ConnectionFailed, "Connection failed (NAT traversal may not be possible)");
            }
        }
    }

    // === WebRTC Signal Handlers ===

    private void OnSessionDescriptionCreated(string type, string sdp)
    {
        GD.Print($"Session description created: type={type}");
        _localSdpType = type;
        _localSdp = sdp;
        _rtcPeer.SetLocalDescription(type, sdp);

        // After SetLocalDescription, ICE gathering begins
        _iceGatheringStarted = true;
        _iceGatherTimer = 0;
    }

    private void OnIceCandidateCreated(string media, long index, string name)
    {
        _iceCandidates.Add(new Dictionary<string, object>
        {
            ["media"] = media,
            ["index"] = (int)index,
            ["name"] = name
        });

        if (!_firstCandidateReceived)
        {
            _firstCandidateReceived = true;
            _iceGatherTimer = 0; // Start timeout from first candidate
        }

        GD.Print($"ICE candidate gathered ({_iceCandidates.Count} total)");
    }

    private void OnIceGatheringComplete()
    {
        if (_phase != ConnectionPhase.CreatingOffer && _phase != ConnectionPhase.CreatingAnswer)
            return;

        GD.Print($"ICE gathering complete. {_iceCandidates.Count} candidates collected.");

        // Generate session code
        string sessionCode = GenerateSessionCode();

        if (_phase == ConnectionPhase.CreatingOffer)
        {
            SetPhase(ConnectionPhase.WaitingForAnswer);
        }
        else if (_phase == ConnectionPhase.CreatingAnswer)
        {
            SetPhase(ConnectionPhase.WaitingForConnection);
        }

        EmitSignal(SignalName.SessionCodeReady, sessionCode);
    }

    // === Session Code Encoding/Decoding ===

    private class SessionData
    {
        public string Type { get; set; }
        public string Sdp { get; set; }
        public List<IceCandidate> Candidates { get; set; } = new();
    }

    private class IceCandidate
    {
        public string Media { get; set; }
        public int Index { get; set; }
        public string Name { get; set; }
    }

    private string GenerateSessionCode()
    {
        var data = new SessionData
        {
            Type = _localSdpType,
            Sdp = _localSdp,
            Candidates = new List<IceCandidate>()
        };

        foreach (var c in _iceCandidates)
        {
            data.Candidates.Add(new IceCandidate
            {
                Media = (string)c["media"],
                Index = (int)c["index"],
                Name = (string)c["name"]
            });
        }

        string json = JsonSerializer.Serialize(data);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        // Compress with DEFLATE
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static SessionData DecodeSessionCode(string code)
    {
        // Clean up whitespace
        code = code.Trim().Replace("\n", "").Replace("\r", "").Replace(" ", "");

        byte[] compressed = Convert.FromBase64String(code);

        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);

        string json = Encoding.UTF8.GetString(output.ToArray());
        return JsonSerializer.Deserialize<SessionData>(json);
    }

    // === Multiplayer Callbacks ===

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        if (IsHost)
        {
            ClientPeerId = id;
        }

        SetPhase(ConnectionPhase.Connected);
        EmitSignal(SignalName.PeerConnected, id);
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");

        if (IsHost && id == ClientPeerId)
        {
            ClientPeerId = 0;
        }

        EmitSignal(SignalName.PeerDisconnected, id);
    }

    private void OnConnectedToServer()
    {
        GD.Print("Connected to server");
        SetPhase(ConnectionPhase.Connected);
        EmitSignal(SignalName.ConnectionSucceeded);
    }

    private void OnConnectionFailed()
    {
        GD.Print("Connection failed");
        Disconnect();
        EmitSignal(SignalName.ConnectionFailed, "Failed to connect");
    }

    private void OnServerDisconnected()
    {
        GD.Print("Server disconnected");
        Disconnect();
        EmitSignal(SignalName.PeerDisconnected, HostPeerId);
    }

    // === RPCs ===

    // Host sends initial board state to client
    public void SendGameStart(SetupMode mode, string boardState)
    {
        if (!IsHost) return;

        RpcId(ClientPeerId, MethodName.ReceiveGameStart, (int)mode, boardState);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveGameStart(int setupMode, string boardState)
    {
        GD.Print($"Received game start: mode={setupMode}");
        EmitSignal(SignalName.GameStartReceived, setupMode, boardState);
    }

    // Client requests a move from host
    public void RequestMove(Vector2I from, Vector2I to, PieceType? promotion = null)
    {
        int promoType = promotion.HasValue ? (int)promotion.Value : -1;

        if (IsHost)
        {
            // Host validates locally, no RPC needed
            EmitSignal(SignalName.MoveRequestReceived, from.X, from.Y, to.X, to.Y, promoType);
        }
        else
        {
            // Client sends to host
            RpcId(HostPeerId, MethodName.ReceiveMoveRequest, from.X, from.Y, to.X, to.Y, promoType);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMoveRequest(int fromX, int fromY, int toX, int toY, int promotionType)
    {
        // Only host processes move requests
        if (!IsHost) return;

        GD.Print($"Received move request: ({fromX},{fromY}) -> ({toX},{toY})");
        EmitSignal(SignalName.MoveRequestReceived, fromX, fromY, toX, toY, promotionType);
    }

    // Host confirms a move to all peers
    public void ConfirmMove(Vector2I from, Vector2I to, PieceType? promotion = null)
    {
        if (!IsHost) return;

        int promoType = promotion.HasValue ? (int)promotion.Value : -1;

        // Send to client
        RpcId(ClientPeerId, MethodName.ReceiveMoveConfirm, from.X, from.Y, to.X, to.Y, promoType);

        // Also emit locally for host
        EmitSignal(SignalName.MoveConfirmed, from.X, from.Y, to.X, to.Y, promoType);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMoveConfirm(int fromX, int fromY, int toX, int toY, int promotionType)
    {
        GD.Print($"Received move confirm: ({fromX},{fromY}) -> ({toX},{toY})");
        EmitSignal(SignalName.MoveConfirmed, fromX, fromY, toX, toY, promotionType);
    }

    // Host sends game end
    public void SendGameEnd(GameState result)
    {
        if (!IsHost) return;

        Rpc(MethodName.ReceiveGameEnd, (int)result);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveGameEnd(int result)
    {
        GD.Print($"Game ended: {(GameState)result}");
        EmitSignal(SignalName.GameEnded, result);
    }

    // Serialize board state for network transfer
    public static string SerializeBoardState(Piece[,] board)
    {
        var parts = new List<string>();

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                Piece piece = board[file, rank];
                if (piece != null)
                {
                    string color = piece.IsWhite ? "w" : "b";
                    string type = piece.Type switch
                    {
                        PieceType.King => "K",
                        PieceType.Queen => "Q",
                        PieceType.Rook => "R",
                        PieceType.Bishop => "B",
                        PieceType.Knight => "N",
                        PieceType.Pawn => "P",
                        _ => "?"
                    };
                    bool moved = piece.HasMoved ? "1" == "1" : false;
                    parts.Add($"{file},{rank},{color}{type},{(moved ? 1 : 0)}");
                }
            }
        }

        return string.Join(";", parts);
    }

    // Deserialize board state from network
    public static void DeserializeBoardState(string data, Board board)
    {
        board.ClearBoard();

        if (string.IsNullOrEmpty(data)) return;

        var parts = data.Split(';');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            var tokens = part.Split(',');
            if (tokens.Length < 4) continue;

            int file = int.Parse(tokens[0]);
            int rank = int.Parse(tokens[1]);
            string pieceCode = tokens[2];
            bool hasMoved = tokens[3] == "1";

            bool isWhite = pieceCode[0] == 'w';
            char typeChar = pieceCode[1];

            Vector2I pos = new Vector2I(file, rank);
            Piece piece = typeChar switch
            {
                'K' => new King(isWhite, pos),
                'Q' => new Queen(isWhite, pos),
                'R' => new Rook(isWhite, pos),
                'B' => new Bishop(isWhite, pos),
                'N' => new Knight(isWhite, pos),
                'P' => new Pawn(isWhite, pos),
                _ => null
            };

            if (piece != null)
            {
                piece.HasMoved = hasMoved;
                board.SetPiece(pos, piece);
            }
        }
    }
}
