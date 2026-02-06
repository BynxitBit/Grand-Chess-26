using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public enum SetupMode
{
    TwoLines,
    OneLine,
    ThreeLines,
    Custom
}

public static class SetupManager
{
    private static Random _random = new Random();

    public static int PawnFirstMoveDistance { get; set; } = 2;

    public static void SetupBoard(Board board, SetupMode mode)
    {
        // Custom mode doesn't reset the board - it uses whatever was placed
        if (mode != SetupMode.Custom)
        {
            board.ClearBoard();
        }

        switch (mode)
        {
            case SetupMode.TwoLines:
                SetupTwoLines(board);
                PawnFirstMoveDistance = 2;
                break;
            case SetupMode.OneLine:
                SetupOneLine(board);
                PawnFirstMoveDistance = 2;
                break;
            case SetupMode.ThreeLines:
                SetupThreeLines(board);
                PawnFirstMoveDistance = 3; // Larger pawn moves for dense setup
                break;
            case SetupMode.Custom:
                // Board already has pieces from SetupEditor
                PawnFirstMoveDistance = 2;
                break;
        }

        // Update pawn move distance
        Pawn.FirstMoveDistance = PawnFirstMoveDistance;
    }

    #region One Line Setup (Chess960-style)

    private static void SetupOneLine(Board board)
    {
        // Generate randomized back rank pattern
        PieceType[] backRankPattern = GenerateOneLineBackRank();

        SetupOneLineSide(board, true, backRankPattern);  // White
        SetupOneLineSide(board, false, backRankPattern); // Black (mirror)
    }

    private static PieceType[] GenerateOneLineBackRank()
    {
        PieceType[] pattern = new PieceType[Board.BoardSize];

        // Track available positions
        List<int> available = new List<int>();
        for (int i = 0; i < Board.BoardSize; i++)
            available.Add(i);

        // 1. Place bishops on opposite colors (need multiple pairs for 28 squares)
        int numBishopPairs = 4;
        List<int> lightSquares = new List<int>();
        List<int> darkSquares = new List<int>();

        foreach (int pos in available)
        {
            if (pos % 2 == 0)
                lightSquares.Add(pos);
            else
                darkSquares.Add(pos);
        }

        for (int i = 0; i < numBishopPairs; i++)
        {
            int lightIdx = _random.Next(lightSquares.Count);
            int lightPos = lightSquares[lightIdx];
            lightSquares.RemoveAt(lightIdx);
            pattern[lightPos] = PieceType.Bishop;
            available.Remove(lightPos);

            int darkIdx = _random.Next(darkSquares.Count);
            int darkPos = darkSquares[darkIdx];
            darkSquares.RemoveAt(darkIdx);
            pattern[darkPos] = PieceType.Bishop;
            available.Remove(darkPos);
        }

        // 2. Place king and rooks - king must be between at least two rooks
        // Sort available positions
        available.Sort();

        // Pick positions for 4 rooks and 1 king, ensuring king is between rooks
        int numRooks = 4;
        List<int> rookKingPositions = new List<int>();
        for (int i = 0; i < numRooks + 1 && available.Count > 0; i++)
        {
            int idx = _random.Next(available.Count);
            rookKingPositions.Add(available[idx]);
            available.RemoveAt(idx);
        }
        rookKingPositions.Sort();

        // King goes in a middle position (not first or last)
        int kingIdx = _random.Next(1, rookKingPositions.Count - 1);
        pattern[rookKingPositions[kingIdx]] = PieceType.King;

        // Rest are rooks
        for (int i = 0; i < rookKingPositions.Count; i++)
        {
            if (i != kingIdx)
                pattern[rookKingPositions[i]] = PieceType.Rook;
        }

        // 3. Place queens
        int numQueens = 3;
        for (int i = 0; i < numQueens && available.Count > 0; i++)
        {
            int idx = _random.Next(available.Count);
            pattern[available[idx]] = PieceType.Queen;
            available.RemoveAt(idx);
        }

        // 4. Place knights in remaining positions
        foreach (int pos in available)
        {
            pattern[pos] = PieceType.Knight;
        }

        return pattern;
    }

    private static void SetupOneLineSide(Board board, bool isWhite, PieceType[] backRankPattern)
    {
        int backRank = isWhite ? 0 : Board.BoardSize - 1;
        int pawnRank = isWhite ? 1 : Board.BoardSize - 2;

        // Place back rank pieces according to pattern
        for (int file = 0; file < Board.BoardSize; file++)
        {
            Piece piece = backRankPattern[file] switch
            {
                PieceType.King => new King(isWhite, new Vector2I(file, backRank)),
                PieceType.Queen => new Queen(isWhite, new Vector2I(file, backRank)),
                PieceType.Rook => new Rook(isWhite, new Vector2I(file, backRank)),
                PieceType.Bishop => new Bishop(isWhite, new Vector2I(file, backRank)),
                PieceType.Knight => new Knight(isWhite, new Vector2I(file, backRank)),
                _ => new Knight(isWhite, new Vector2I(file, backRank))
            };
            board.SetPiece(new Vector2I(file, backRank), piece);
        }

        // Pawns on second rank
        for (int file = 0; file < Board.BoardSize; file++)
        {
            board.SetPiece(new Vector2I(file, pawnRank), new Pawn(isWhite, new Vector2I(file, pawnRank)));
        }
    }

    #endregion

    #region Two Lines Setup

    private static void SetupTwoLines(Board board)
    {
        SetupTwoLinesSide(board, true);  // White
        SetupTwoLinesSide(board, false); // Black
    }

    private static void SetupTwoLinesSide(Board board, bool isWhite)
    {
        int backRank1 = isWhite ? 0 : Board.BoardSize - 1;
        int backRank2 = isWhite ? 1 : Board.BoardSize - 2;
        int pawnRank = isWhite ? 2 : Board.BoardSize - 3;

        // Back rank 1: King in center, flanked by major pieces
        // Pattern: R N B Q K Q B N R (repeated to fill 28)
        int center = Board.BoardSize / 2;

        // King always on back-most rank, centered
        board.SetPiece(new Vector2I(center, backRank1), new King(isWhite, new Vector2I(center, backRank1)));

        // Build symmetric pattern outward from king
        int[] piecePattern = { 1, 2, 3, 4 }; // Q, B, N, R pattern

        for (int offset = 1; offset <= center; offset++)
        {
            int leftFile = center - offset;
            int rightFile = center + offset;
            int patternIdx = (offset - 1) % 4;

            if (leftFile >= 0)
            {
                Piece leftPiece = CreatePieceByPattern(piecePattern[patternIdx], isWhite, new Vector2I(leftFile, backRank1));
                board.SetPiece(new Vector2I(leftFile, backRank1), leftPiece);
            }

            if (rightFile < Board.BoardSize)
            {
                Piece rightPiece = CreatePieceByPattern(piecePattern[patternIdx], isWhite, new Vector2I(rightFile, backRank1));
                board.SetPiece(new Vector2I(rightFile, backRank1), rightPiece);
            }
        }

        // Back rank 2: More varied pieces (no king)
        // Pattern: B N R Q Q R N B (repeated)
        int[] rank2Pattern = { 2, 3, 4, 1, 1, 4, 3, 2 };

        for (int file = 0; file < Board.BoardSize; file++)
        {
            int patternIdx = file % rank2Pattern.Length;
            Piece piece = CreatePieceByPattern(rank2Pattern[patternIdx], isWhite, new Vector2I(file, backRank2));
            board.SetPiece(new Vector2I(file, backRank2), piece);
        }

        // Pawns
        for (int file = 0; file < Board.BoardSize; file++)
        {
            board.SetPiece(new Vector2I(file, pawnRank), new Pawn(isWhite, new Vector2I(file, pawnRank)));
        }
    }

    #endregion

    #region Three Lines Setup

    private static void SetupThreeLines(Board board)
    {
        SetupThreeLinesSide(board, true);  // White
        SetupThreeLinesSide(board, false); // Black
    }

    private static void SetupThreeLinesSide(Board board, bool isWhite)
    {
        int backRank1 = isWhite ? 0 : Board.BoardSize - 1;
        int backRank2 = isWhite ? 1 : Board.BoardSize - 2;
        int backRank3 = isWhite ? 2 : Board.BoardSize - 3;
        int pawnRank = isWhite ? 3 : Board.BoardSize - 4;

        int center = Board.BoardSize / 2;

        // Back rank 1: King centered, heavy pieces (rooks, queens)
        board.SetPiece(new Vector2I(center, backRank1), new King(isWhite, new Vector2I(center, backRank1)));

        for (int file = 0; file < Board.BoardSize; file++)
        {
            if (file == center) continue; // Skip king position

            // Alternate between rooks and queens
            Piece piece;
            if (file % 3 == 0)
                piece = new Rook(isWhite, new Vector2I(file, backRank1));
            else if (file % 3 == 1)
                piece = new Queen(isWhite, new Vector2I(file, backRank1));
            else
                piece = new Rook(isWhite, new Vector2I(file, backRank1));

            board.SetPiece(new Vector2I(file, backRank1), piece);
        }

        // Back rank 2: Mix of all piece types except king
        int[] rank2Pattern = { 1, 2, 3, 4, 2, 3, 1, 4 }; // Q, B, N, R pattern
        for (int file = 0; file < Board.BoardSize; file++)
        {
            int patternIdx = file % rank2Pattern.Length;
            Piece piece = CreatePieceByPattern(rank2Pattern[patternIdx], isWhite, new Vector2I(file, backRank2));
            board.SetPiece(new Vector2I(file, backRank2), piece);
        }

        // Back rank 3: Knights and bishops (light pieces)
        for (int file = 0; file < Board.BoardSize; file++)
        {
            Piece piece;
            if (file % 2 == 0)
                piece = new Knight(isWhite, new Vector2I(file, backRank3));
            else
                piece = new Bishop(isWhite, new Vector2I(file, backRank3));

            board.SetPiece(new Vector2I(file, backRank3), piece);
        }

        // Pawns on rank 4
        for (int file = 0; file < Board.BoardSize; file++)
        {
            board.SetPiece(new Vector2I(file, pawnRank), new Pawn(isWhite, new Vector2I(file, pawnRank)));
        }
    }

    #endregion

    #region Helper Methods

    private static Piece CreatePieceByPattern(int pattern, bool isWhite, Vector2I position)
    {
        return pattern switch
        {
            1 => new Queen(isWhite, position),
            2 => new Bishop(isWhite, position),
            3 => new Knight(isWhite, position),
            4 => new Rook(isWhite, position),
            _ => new Knight(isWhite, position)
        };
    }

    public static string GetModeName(SetupMode mode)
    {
        return mode switch
        {
            SetupMode.TwoLines => "Two Lines",
            SetupMode.OneLine => "One Line",
            SetupMode.ThreeLines => "Three Lines",
            SetupMode.Custom => "Custom Setup",
            _ => "Unknown"
        };
    }

    public static string GetModeDescription(SetupMode mode)
    {
        return mode switch
        {
            SetupMode.TwoLines => "Two ranks of major pieces.\nSlower development, more tactical.",
            SetupMode.OneLine => "Randomized back rank (Chess960-style).\nKing between rooks, bishops on opposite colors.",
            SetupMode.ThreeLines => "Three ranks of major pieces!\nExtremely dense, chaotic battles.\nPawns can move 3 squares initially.",
            SetupMode.Custom => "Create your own starting position!\nPlace pieces manually on the board.",
            _ => ""
        };
    }

    #endregion
}
