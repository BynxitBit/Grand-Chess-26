using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public enum GameState
{
    Playing,
    WhiteWins,
    BlackWins,
    Stalemate,
    Draw
}

public partial class GameManager : Node
{
    private Board _board;
    private bool _whiteTurn = true;
    private GameState _gameState = GameState.Playing;
    private Vector2I? _enPassantTarget = null;
    private int _moveCount = 0;
    private int _halfMoveClock = 0; // For 50-move rule (extended to 100 for large board)

    [Signal]
    public delegate void TurnChangedEventHandler(bool isWhiteTurn);

    [Signal]
    public delegate void GameOverEventHandler(long state);

    [Signal]
    public delegate void CheckEventHandler(bool isWhiteInCheck);

    [Signal]
    public delegate void MoveExecutedEventHandler(string notation);

    [Signal]
    public delegate void PawnPromotionRequestedEventHandler(Vector2I square, bool isWhite);

    public bool IsWhiteTurn => _whiteTurn;

    // Promotion state
    private bool _awaitingPromotion = false;
    private Vector2I _promotionSquare;
    private Piece _promotingPawn;
    private Vector2I _promotionFrom;
    private Piece _promotionCaptured;

    public bool IsAwaitingPromotion => _awaitingPromotion;
    public GameState CurrentState => _gameState;
    public SetupMode CurrentSetupMode { get; set; } = SetupMode.TwoLines;

    public void Initialize(Board board)
    {
        _board = board;
    }

    public void SetupGame(SetupMode mode)
    {
        CurrentSetupMode = mode;
        SetupManager.SetupBoard(_board, mode);

        _whiteTurn = true;
        _gameState = GameState.Playing;
        _moveCount = 0;
        _halfMoveClock = 0;
        _enPassantTarget = null;
    }

    public void SetupTwoLines()
    {
        SetupGame(SetupMode.TwoLines);
    }

    public List<Vector2I> GetLegalMoves(Vector2I square)
    {
        Piece piece = _board.GetPiece(square);
        if (piece == null || piece.IsWhite != _whiteTurn)
        {
            return new List<Vector2I>();
        }

        var possibleMoves = piece.GetPossibleMoves(_board.GetBoardState());
        var legalMoves = new List<Vector2I>();

        foreach (var move in possibleMoves)
        {
            if (IsMoveLegal(square, move))
            {
                legalMoves.Add(move);
            }
        }

        // Add en passant moves for pawns
        if (piece is Pawn && _enPassantTarget.HasValue)
        {
            int direction = piece.IsWhite ? 1 : -1;
            if (Math.Abs(_enPassantTarget.Value.X - square.X) == 1 &&
                _enPassantTarget.Value.Y == square.Y + direction)
            {
                if (IsMoveLegal(square, _enPassantTarget.Value))
                {
                    legalMoves.Add(_enPassantTarget.Value);
                }
            }
        }

        return legalMoves;
    }

    private bool IsMoveLegal(Vector2I from, Vector2I to)
    {
        // Simulate the move and check if king is in check
        var boardState = _board.GetBoardState();
        Piece piece = boardState[from.X, from.Y];
        Piece captured = boardState[to.X, to.Y];

        // Make temporary move
        boardState[to.X, to.Y] = piece;
        boardState[from.X, from.Y] = null;
        Vector2I originalPos = piece.Position;
        piece.Position = to;

        // Handle en passant capture
        Piece enPassantCaptured = null;
        if (piece is Pawn && _enPassantTarget.HasValue && to == _enPassantTarget.Value)
        {
            int capturedPawnRank = piece.IsWhite ? to.Y - 1 : to.Y + 1;
            enPassantCaptured = boardState[to.X, capturedPawnRank];
            boardState[to.X, capturedPawnRank] = null;
        }

        bool isLegal = !IsKingInCheck(piece.IsWhite, boardState);

        // Undo temporary move
        boardState[from.X, from.Y] = piece;
        boardState[to.X, to.Y] = captured;
        piece.Position = originalPos;

        if (enPassantCaptured != null)
        {
            int capturedPawnRank = piece.IsWhite ? to.Y - 1 : to.Y + 1;
            boardState[to.X, capturedPawnRank] = enPassantCaptured;
        }

        // Additional check for castling - king cannot castle through check
        if (piece is King && Math.Abs(to.X - from.X) == 2)
        {
            int direction = to.X > from.X ? 1 : -1;
            Vector2I intermediate = new Vector2I(from.X + direction, from.Y);
            if (!IsMoveLegal(from, intermediate))
            {
                return false;
            }
            if (IsKingInCheck(piece.IsWhite, _board.GetBoardState()))
            {
                return false;
            }
        }

        return isLegal;
    }

    public bool TryMakeMove(Vector2I from, Vector2I to)
    {
        var legalMoves = GetLegalMoves(from);
        if (!legalMoves.Contains(to))
        {
            return false;
        }

        Piece piece = _board.GetPiece(from);
        Piece captured = _board.GetPiece(to);

        // Handle en passant capture
        if (piece is Pawn && _enPassantTarget.HasValue && to == _enPassantTarget.Value)
        {
            int capturedPawnRank = piece.IsWhite ? to.Y - 1 : to.Y + 1;
            _board.SetPiece(new Vector2I(to.X, capturedPawnRank), null);
            captured = _board.GetPiece(new Vector2I(to.X, capturedPawnRank));
        }

        // Handle castling
        if (piece is King && Math.Abs(to.X - from.X) == 2)
        {
            int direction = to.X > from.X ? 1 : -1;
            // Find the rook
            int rookFromFile = direction > 0 ? Board.BoardSize - 1 : 0;
            for (int f = from.X + direction; direction > 0 ? f < Board.BoardSize : f >= 0; f += direction)
            {
                Piece maybRook = _board.GetPiece(new Vector2I(f, from.Y));
                if (maybRook is Rook)
                {
                    rookFromFile = f;
                    break;
                }
            }
            int rookToFile = to.X - direction;
            _board.MovePiece(new Vector2I(rookFromFile, from.Y), new Vector2I(rookToFile, from.Y));
        }

        // Set en passant target for pawn double moves
        _enPassantTarget = null;
        if (piece is Pawn && Math.Abs(to.Y - from.Y) >= 2)
        {
            int enPassantRank = from.Y + (piece.IsWhite ? 1 : -1);
            _enPassantTarget = new Vector2I(to.X, enPassantRank);
        }

        // Execute the move
        _board.MovePiece(from, to);

        // Handle pawn promotion
        if (piece is Pawn pawn && pawn.CanPromote())
        {
            // Store state and request promotion choice
            _awaitingPromotion = true;
            _promotionSquare = to;
            _promotingPawn = piece;
            _promotionFrom = from;
            _promotionCaptured = captured;
            EmitSignal(SignalName.PawnPromotionRequested, to, piece.IsWhite);
            return true; // Move started, waiting for promotion choice
        }

        // Update game state
        _moveCount++;
        if (captured != null || piece is Pawn)
        {
            _halfMoveClock = 0;
        }
        else
        {
            _halfMoveClock++;
        }

        // Build move notation
        string notation = BuildMoveNotation(piece, from, to, captured != null);
        EmitSignal(SignalName.MoveExecuted, notation);

        // Switch turns
        _whiteTurn = !_whiteTurn;
        EmitSignal(SignalName.TurnChanged, _whiteTurn);

        // Check for check/checkmate/stalemate
        UpdateGameState();

        return true;
    }

    private string BuildMoveNotation(Piece piece, Vector2I from, Vector2I to, bool isCapture)
    {
        string notation = "";

        if (piece is King && Math.Abs(to.X - from.X) == 2)
        {
            notation = to.X > from.X ? "O-O" : "O-O-O";
        }
        else
        {
            if (piece is not Pawn)
            {
                notation += piece.Type switch
                {
                    PieceType.King => "K",
                    PieceType.Queen => "Q",
                    PieceType.Rook => "R",
                    PieceType.Bishop => "B",
                    PieceType.Knight => "N",
                    _ => ""
                };
            }

            if (isCapture)
            {
                if (piece is Pawn)
                {
                    notation += Board.GetFileLabel(from.X);
                }
                notation += "x";
            }

            notation += Board.GetSquareNotation(to);
        }

        // Add check/checkmate symbols
        if (IsKingInCheck(!piece.IsWhite, _board.GetBoardState()))
        {
            // Use internal method that assumes opponent's turn for proper checkmate detection
            if (IsCheckmateInternal(!piece.IsWhite, !piece.IsWhite))
            {
                notation += "#";
            }
            else
            {
                notation += "+";
            }
        }

        return notation;
    }

    private void UpdateGameState()
    {
        bool currentPlayerInCheck = IsKingInCheck(_whiteTurn, _board.GetBoardState());

        if (currentPlayerInCheck)
        {
            EmitSignal(SignalName.Check, _whiteTurn);

            // Highlight king in check
            Vector2I? kingPos = FindKingPosition(_whiteTurn);
            _board.SetKingInCheck(kingPos);
        }
        else
        {
            _board.SetKingInCheck(null);
        }

        if (IsCheckmate(_whiteTurn))
        {
            _gameState = _whiteTurn ? GameState.BlackWins : GameState.WhiteWins;
            EmitSignal(SignalName.GameOver, (int)_gameState);
        }
        else if (IsStalemate(_whiteTurn))
        {
            _gameState = GameState.Stalemate;
            EmitSignal(SignalName.GameOver, (int)_gameState);
        }
        else if (_halfMoveClock >= 100) // 100-move rule for large board
        {
            _gameState = GameState.Draw;
            EmitSignal(SignalName.GameOver, (int)_gameState);
        }
    }

    private bool IsKingInCheck(bool isWhite, Piece[,] board)
    {
        Vector2I? kingPos = null;

        // Find the king
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                Piece p = board[file, rank];
                if (p is King && p.IsWhite == isWhite)
                {
                    kingPos = new Vector2I(file, rank);
                    break;
                }
            }
            if (kingPos.HasValue) break;
        }

        if (!kingPos.HasValue) return false;

        // Check if any enemy piece can attack the king
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                Piece p = board[file, rank];
                if (p != null && p.IsWhite != isWhite)
                {
                    var moves = p.GetPossibleMoves(board);
                    if (moves.Contains(kingPos.Value))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private Vector2I? FindKingPosition(bool isWhite)
    {
        var board = _board.GetBoardState();
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                Piece p = board[file, rank];
                if (p is King && p.IsWhite == isWhite)
                {
                    return new Vector2I(file, rank);
                }
            }
        }
        return null;
    }

    private bool IsCheckmate(bool isWhite)
    {
        return IsCheckmateInternal(isWhite, isWhite);
    }

    // Version that doesn't depend on _whiteTurn for notation purposes
    private bool IsCheckmateInternal(bool isWhite, bool assumeTurn)
    {
        if (!IsKingInCheck(isWhite, _board.GetBoardState()))
        {
            return false;
        }

        return !HasAnyLegalMovesInternal(isWhite, assumeTurn);
    }

    private bool IsStalemate(bool isWhite)
    {
        if (IsKingInCheck(isWhite, _board.GetBoardState()))
        {
            return false;
        }

        return !HasAnyLegalMovesInternal(isWhite, isWhite);
    }

    private bool HasAnyLegalMoves(bool isWhite)
    {
        return HasAnyLegalMovesInternal(isWhite, _whiteTurn);
    }

    // Internal method that doesn't depend on _whiteTurn for notation purposes
    private bool HasAnyLegalMovesInternal(bool isWhite, bool assumeTurn)
    {
        var board = _board.GetBoardState();

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                Piece p = board[file, rank];
                if (p != null && p.IsWhite == isWhite)
                {
                    var moves = GetLegalMovesForPlayer(new Vector2I(file, rank), isWhite);
                    if (moves.Count > 0)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Get legal moves for a specific player (doesn't depend on _whiteTurn)
    private List<Vector2I> GetLegalMovesForPlayer(Vector2I square, bool isWhite)
    {
        Piece piece = _board.GetPiece(square);
        if (piece == null || piece.IsWhite != isWhite)
        {
            return new List<Vector2I>();
        }

        var possibleMoves = piece.GetPossibleMoves(_board.GetBoardState());
        var legalMoves = new List<Vector2I>();

        foreach (var move in possibleMoves)
        {
            if (IsMoveLegal(square, move))
            {
                legalMoves.Add(move);
            }
        }

        // Add en passant moves for pawns
        if (piece is Pawn && _enPassantTarget.HasValue)
        {
            int direction = piece.IsWhite ? 1 : -1;
            if (Math.Abs(_enPassantTarget.Value.X - square.X) == 1 &&
                _enPassantTarget.Value.Y == square.Y + direction)
            {
                if (IsMoveLegal(square, _enPassantTarget.Value))
                {
                    legalMoves.Add(_enPassantTarget.Value);
                }
            }
        }

        return legalMoves;
    }

    public void ResetGame()
    {
        SetupGame(CurrentSetupMode);
        _board.ClearHighlights();
        _awaitingPromotion = false;
        EmitSignal(SignalName.TurnChanged, true);
    }

    // Reset turn state without setting up the board (for multiplayer sync)
    public void ResetTurnState()
    {
        _whiteTurn = true;
        _gameState = GameState.Playing;
        _moveCount = 0;
        _halfMoveClock = 0;
        _enPassantTarget = null;
        _awaitingPromotion = false;
        _board.ClearHighlights();
    }

    // Set the current turn (for FEN import)
    public void SetTurn(bool isWhiteTurn)
    {
        _whiteTurn = isWhiteTurn;
        EmitSignal(SignalName.TurnChanged, _whiteTurn);
    }

    public void CompletePromotion(PieceType promotionType)
    {
        if (!_awaitingPromotion)
            return;

        _awaitingPromotion = false;

        // Create the promoted piece
        Piece promotedPiece = promotionType switch
        {
            PieceType.Queen => new Queen(_promotingPawn.IsWhite, _promotionSquare),
            PieceType.Rook => new Rook(_promotingPawn.IsWhite, _promotionSquare),
            PieceType.Bishop => new Bishop(_promotingPawn.IsWhite, _promotionSquare),
            PieceType.Knight => new Knight(_promotingPawn.IsWhite, _promotionSquare),
            _ => new Queen(_promotingPawn.IsWhite, _promotionSquare)
        };

        _board.SetPiece(_promotionSquare, promotedPiece);

        // Update game state
        _moveCount++;
        if (_promotionCaptured != null || true) // Pawn move always resets clock
        {
            _halfMoveClock = 0;
        }

        // Build move notation with promotion
        string notation = BuildPromotionNotation(_promotionFrom, _promotionSquare, _promotionCaptured != null, promotionType);
        EmitSignal(SignalName.MoveExecuted, notation);

        // Switch turns
        _whiteTurn = !_whiteTurn;
        EmitSignal(SignalName.TurnChanged, _whiteTurn);

        // Check for check/checkmate/stalemate
        UpdateGameState();
    }

    private string BuildPromotionNotation(Vector2I from, Vector2I to, bool isCapture, PieceType promotionType)
    {
        string notation = "";

        if (isCapture)
        {
            notation += Board.GetFileLabel(from.X);
            notation += "x";
        }

        notation += Board.GetSquareNotation(to);

        // Add promotion piece
        notation += "=" + promotionType switch
        {
            PieceType.Queen => "Q",
            PieceType.Rook => "R",
            PieceType.Bishop => "B",
            PieceType.Knight => "N",
            _ => "Q"
        };

        // Add check/checkmate symbols
        bool opponentIsWhite = !_promotingPawn.IsWhite;
        if (IsKingInCheck(opponentIsWhite, _board.GetBoardState()))
        {
            // Use internal method that assumes opponent's turn for proper checkmate detection
            if (IsCheckmateInternal(opponentIsWhite, opponentIsWhite))
            {
                notation += "#";
            }
            else
            {
                notation += "+";
            }
        }

        return notation;
    }
}
