using Godot;
using System.Collections.Generic;

namespace GrandChess26;

public partial class NetworkManager : Node
{
    public const int DefaultPort = 7777;
    public const int MaxClients = 1; // 1v1 only

    private ENetMultiplayerPeer _peer;

    // Player assignments
    public long HostPeerId { get; private set; } = 1;
    public long ClientPeerId { get; private set; } = 0;
    public bool IsHost => Multiplayer.IsServer();
    public bool IsClient => !Multiplayer.IsServer() && Multiplayer.HasMultiplayerPeer();
    public bool IsOnline => Multiplayer.HasMultiplayerPeer();
    public long MyPeerId => Multiplayer.GetUniqueId();

    // Host is always White, Client is always Black
    public bool AmIWhite => IsHost;

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

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public Error HostGame(int port = DefaultPort)
    {
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(port, MaxClients);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to create server: {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        HostPeerId = 1;
        GD.Print($"Server started on port {port}");
        return Error.Ok;
    }

    public Error JoinGame(string address, int port = DefaultPort)
    {
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to connect to {address}:{port} - {error}");
            return error;
        }

        Multiplayer.MultiplayerPeer = _peer;
        GD.Print($"Connecting to {address}:{port}...");
        return Error.Ok;
    }

    public void Disconnect()
    {
        if (_peer != null)
        {
            _peer.Close();
            Multiplayer.MultiplayerPeer = null;
            _peer = null;
        }
        ClientPeerId = 0;
        GD.Print("Disconnected from network");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");

        if (IsHost)
        {
            ClientPeerId = id;
        }

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
        EmitSignal(SignalName.ConnectionSucceeded);
    }

    private void OnConnectionFailed()
    {
        GD.Print("Connection failed");
        Disconnect();
        EmitSignal(SignalName.ConnectionFailed, "Failed to connect to server");
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
