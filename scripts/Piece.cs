using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public enum PieceType
{
    King,
    Queen,
    Rook,
    Bishop,
    Knight,
    Pawn,
    Archbishop,  // Bishop + Knight
    Chancellor,  // Rook + Knight
    Nightrider,  // Extends knight jumps in a line
    Cannon,      // Rook move, captures by jumping over 1 piece
    Camel        // (3,1) leaper
}

public abstract class Piece
{
    public bool IsWhite { get; set; }
    public Vector2I Position { get; set; }
    public bool HasMoved { get; set; } = false;
    public abstract PieceType Type { get; }

    protected Piece(bool isWhite, Vector2I position)
    {
        IsWhite = isWhite;
        Position = position;
    }

    public abstract string GetUnicodeSymbol();

    public abstract List<Vector2I> GetPossibleMoves(Piece[,] board);

    protected bool IsValidSquare(int file, int rank)
    {
        return file >= 0 && file < Board.BoardSize && rank >= 0 && rank < Board.BoardSize;
    }

    protected bool IsEnemyPiece(Piece other)
    {
        return other != null && other.IsWhite != IsWhite;
    }

    protected bool IsFriendlyPiece(Piece other)
    {
        return other != null && other.IsWhite == IsWhite;
    }

    protected List<Vector2I> GetSlidingMoves(Piece[,] board, int[] dirX, int[] dirY)
    {
        var moves = new List<Vector2I>();

        for (int d = 0; d < dirX.Length; d++)
        {
            int file = Position.X + dirX[d];
            int rank = Position.Y + dirY[d];

            while (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];

                if (target == null)
                {
                    moves.Add(new Vector2I(file, rank));
                }
                else if (IsEnemyPiece(target))
                {
                    moves.Add(new Vector2I(file, rank));
                    break;
                }
                else
                {
                    break;
                }

                file += dirX[d];
                rank += dirY[d];
            }
        }

        return moves;
    }

    public Piece Clone()
    {
        Piece clone = Type switch
        {
            PieceType.King => new King(IsWhite, Position),
            PieceType.Queen => new Queen(IsWhite, Position),
            PieceType.Rook => new Rook(IsWhite, Position),
            PieceType.Bishop => new Bishop(IsWhite, Position),
            PieceType.Knight => new Knight(IsWhite, Position),
            PieceType.Pawn => new Pawn(IsWhite, Position),
            PieceType.Archbishop => new Archbishop(IsWhite, Position),
            PieceType.Chancellor => new Chancellor(IsWhite, Position),
            PieceType.Nightrider => new Nightrider(IsWhite, Position),
            PieceType.Cannon => new Cannon(IsWhite, Position),
            PieceType.Camel => new Camel(IsWhite, Position),
            _ => throw new InvalidOperationException()
        };
        clone.HasMoved = HasMoved;
        return clone;
    }
}

public class King : Piece
{
    public override PieceType Type => PieceType.King;

    public King(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2654" : "\u265A";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int[] dx = { -1, -1, -1, 0, 0, 1, 1, 1 };
        int[] dy = { -1, 0, 1, -1, 1, -1, 0, 1 };

        for (int i = 0; i < 8; i++)
        {
            int file = Position.X + dx[i];
            int rank = Position.Y + dy[i];

            if (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null || IsEnemyPiece(target))
                {
                    moves.Add(new Vector2I(file, rank));
                }
            }
        }

        // Castling moves (simplified for 28x28)
        if (!HasMoved)
        {
            // Kingside castling
            if (CanCastleKingside(board))
            {
                moves.Add(new Vector2I(Position.X + 2, Position.Y));
            }
            // Queenside castling
            if (CanCastleQueenside(board))
            {
                moves.Add(new Vector2I(Position.X - 2, Position.Y));
            }
        }

        return moves;
    }

    private bool CanCastleKingside(Piece[,] board)
    {
        // Find rook to the right
        for (int file = Position.X + 1; file < Board.BoardSize; file++)
        {
            Piece piece = board[file, Position.Y];
            if (piece != null)
            {
                if (piece is Rook && piece.IsWhite == IsWhite && !piece.HasMoved)
                {
                    // Check if squares between are empty
                    for (int f = Position.X + 1; f < file; f++)
                    {
                        if (board[f, Position.Y] != null)
                            return false;
                    }
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    private bool CanCastleQueenside(Piece[,] board)
    {
        // Find rook to the left
        for (int file = Position.X - 1; file >= 0; file--)
        {
            Piece piece = board[file, Position.Y];
            if (piece != null)
            {
                if (piece is Rook && piece.IsWhite == IsWhite && !piece.HasMoved)
                {
                    // Check if squares between are empty
                    for (int f = Position.X - 1; f > file; f--)
                    {
                        if (board[f, Position.Y] != null)
                            return false;
                    }
                    return true;
                }
                return false;
            }
        }
        return false;
    }
}

public class Queen : Piece
{
    public override PieceType Type => PieceType.Queen;

    public Queen(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2655" : "\u265B";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        int[] dirX = { -1, -1, -1, 0, 0, 1, 1, 1 };
        int[] dirY = { -1, 0, 1, -1, 1, -1, 0, 1 };
        return GetSlidingMoves(board, dirX, dirY);
    }
}

public class Rook : Piece
{
    public override PieceType Type => PieceType.Rook;

    public Rook(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2656" : "\u265C";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        int[] dirX = { -1, 0, 1, 0 };
        int[] dirY = { 0, -1, 0, 1 };
        return GetSlidingMoves(board, dirX, dirY);
    }
}

public class Bishop : Piece
{
    public override PieceType Type => PieceType.Bishop;

    public Bishop(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2657" : "\u265D";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        int[] dirX = { -1, -1, 1, 1 };
        int[] dirY = { -1, 1, -1, 1 };
        return GetSlidingMoves(board, dirX, dirY);
    }
}

public class Knight : Piece
{
    public override PieceType Type => PieceType.Knight;

    public Knight(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2658" : "\u265E";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int[] dx = { -2, -2, -1, -1, 1, 1, 2, 2 };
        int[] dy = { -1, 1, -2, 2, -2, 2, -1, 1 };

        for (int i = 0; i < 8; i++)
        {
            int file = Position.X + dx[i];
            int rank = Position.Y + dy[i];

            if (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null || IsEnemyPiece(target))
                {
                    moves.Add(new Vector2I(file, rank));
                }
            }
        }

        return moves;
    }
}

public class Pawn : Piece
{
    public override PieceType Type => PieceType.Pawn;

    // Configurable first move distance (up to 4 squares)
    public static int FirstMoveDistance = 4;

    public Pawn(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol()
    {
        return IsWhite ? "\u2659" : "\u265F";
    }

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int direction = IsWhite ? 1 : -1;

        // Forward move
        int newRank = Position.Y + direction;
        if (IsValidSquare(Position.X, newRank) && board[Position.X, newRank] == null)
        {
            moves.Add(new Vector2I(Position.X, newRank));

            // First move: can move 2 (or 3) squares
            if (!HasMoved)
            {
                for (int i = 2; i <= FirstMoveDistance; i++)
                {
                    int doubleRank = Position.Y + direction * i;
                    if (IsValidSquare(Position.X, doubleRank) && board[Position.X, doubleRank] == null)
                    {
                        // Check all squares in between are empty
                        bool pathClear = true;
                        for (int j = 1; j < i; j++)
                        {
                            if (board[Position.X, Position.Y + direction * j] != null)
                            {
                                pathClear = false;
                                break;
                            }
                        }
                        if (pathClear)
                        {
                            moves.Add(new Vector2I(Position.X, doubleRank));
                        }
                    }
                }
            }
        }

        // Diagonal captures
        int[] captureFiles = { Position.X - 1, Position.X + 1 };
        foreach (int file in captureFiles)
        {
            if (IsValidSquare(file, newRank))
            {
                Piece target = board[file, newRank];
                if (target != null && IsEnemyPiece(target))
                {
                    moves.Add(new Vector2I(file, newRank));
                }
            }
        }

        return moves;
    }

    public bool CanPromote()
    {
        int promotionRank = IsWhite ? Board.BoardSize - 1 : 0;
        return Position.Y == promotionRank;
    }
}

public class Archbishop : Piece
{
    public override PieceType Type => PieceType.Archbishop;

    public Archbishop(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol() => IsWhite ? "A" : "a";

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();

        // Bishop moves (diagonal sliding)
        int[] dirX = { -1, -1, 1, 1 };
        int[] dirY = { -1, 1, -1, 1 };
        moves.AddRange(GetSlidingMoves(board, dirX, dirY));

        // Knight jumps
        int[] dx = { -2, -2, -1, -1, 1, 1, 2, 2 };
        int[] dy = { -1, 1, -2, 2, -2, 2, -1, 1 };
        for (int i = 0; i < 8; i++)
        {
            int file = Position.X + dx[i];
            int rank = Position.Y + dy[i];
            if (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null || IsEnemyPiece(target))
                    moves.Add(new Vector2I(file, rank));
            }
        }

        return moves;
    }
}

public class Chancellor : Piece
{
    public override PieceType Type => PieceType.Chancellor;

    public Chancellor(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol() => IsWhite ? "C" : "c";

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();

        // Rook moves (orthogonal sliding)
        int[] dirX = { -1, 0, 1, 0 };
        int[] dirY = { 0, -1, 0, 1 };
        moves.AddRange(GetSlidingMoves(board, dirX, dirY));

        // Knight jumps
        int[] dx = { -2, -2, -1, -1, 1, 1, 2, 2 };
        int[] dy = { -1, 1, -2, 2, -2, 2, -1, 1 };
        for (int i = 0; i < 8; i++)
        {
            int file = Position.X + dx[i];
            int rank = Position.Y + dy[i];
            if (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null || IsEnemyPiece(target))
                    moves.Add(new Vector2I(file, rank));
            }
        }

        return moves;
    }
}

public class Nightrider : Piece
{
    public override PieceType Type => PieceType.Nightrider;

    public Nightrider(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol() => IsWhite ? "NR" : "nr";

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int[] dx = { -2, -2, -1, -1, 1, 1, 2, 2 };
        int[] dy = { -1, 1, -2, 2, -2, 2, -1, 1 };

        for (int dir = 0; dir < 8; dir++)
        {
            int file = Position.X + dx[dir];
            int rank = Position.Y + dy[dir];

            while (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null)
                {
                    moves.Add(new Vector2I(file, rank));
                }
                else if (IsEnemyPiece(target))
                {
                    moves.Add(new Vector2I(file, rank));
                    break;
                }
                else
                {
                    break;
                }
                file += dx[dir];
                rank += dy[dir];
            }
        }

        return moves;
    }
}

public class Cannon : Piece
{
    public override PieceType Type => PieceType.Cannon;

    public Cannon(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol() => IsWhite ? "Cn" : "cn";

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int[] dirX = { -1, 0, 1, 0 };
        int[] dirY = { 0, -1, 0, 1 };

        for (int d = 0; d < 4; d++)
        {
            int file = Position.X + dirX[d];
            int rank = Position.Y + dirY[d];
            bool foundPlatform = false;

            while (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (!foundPlatform)
                {
                    if (target == null)
                    {
                        moves.Add(new Vector2I(file, rank));
                    }
                    else
                    {
                        foundPlatform = true; // This piece is the screen/platform
                    }
                }
                else
                {
                    if (target != null)
                    {
                        if (IsEnemyPiece(target))
                            moves.Add(new Vector2I(file, rank));
                        break; // Stop after first piece beyond platform
                    }
                }

                file += dirX[d];
                rank += dirY[d];
            }
        }

        return moves;
    }
}

public class Camel : Piece
{
    public override PieceType Type => PieceType.Camel;

    public Camel(bool isWhite, Vector2I position) : base(isWhite, position) { }

    public override string GetUnicodeSymbol() => IsWhite ? "Cm" : "cm";

    public override List<Vector2I> GetPossibleMoves(Piece[,] board)
    {
        var moves = new List<Vector2I>();
        int[] dx = { -3, -3, -1, -1, 1, 1, 3, 3 };
        int[] dy = { -1, 1, -3, 3, -3, 3, -1, 1 };

        for (int i = 0; i < 8; i++)
        {
            int file = Position.X + dx[i];
            int rank = Position.Y + dy[i];
            if (IsValidSquare(file, rank))
            {
                Piece target = board[file, rank];
                if (target == null || IsEnemyPiece(target))
                    moves.Add(new Vector2I(file, rank));
            }
        }

        return moves;
    }
}
