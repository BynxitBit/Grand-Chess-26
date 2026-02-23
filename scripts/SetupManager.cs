using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public enum SetupMode
{
    Lines,
    Custom
}

public static class SetupManager
{
    public static int PawnFirstMoveDistance { get; set; } = 4;
    public static int TotalLines { get; set; } = 3;
    public static int PawnLines { get; set; } = 1;

    public static void SetupBoard(Board board, SetupMode mode, bool randomize = false)
    {
        // Custom mode doesn't reset the board - it uses whatever was placed
        if (mode != SetupMode.Custom)
        {
            board.ClearBoard();
        }

        switch (mode)
        {
            case SetupMode.Lines:
                if (randomize)
                    SetupLinesRandomized(board, TotalLines, PawnLines);
                else
                    SetupLines(board, TotalLines, PawnLines);
                break;
            case SetupMode.Custom:
                // Board already has pieces from SetupEditor
                break;
        }

        // Apply current pawn move distance (controlled via UI)
        Pawn.FirstMoveDistance = PawnFirstMoveDistance;
    }

    #region Lines Setup

    private static void SetupLines(Board board, int totalLines, int pawnLines)
    {
        SetupLinesSide(board, true, totalLines, pawnLines);
        SetupLinesSide(board, false, totalLines, pawnLines);
    }

    private static void SetupLinesSide(Board board, bool isWhite, int totalLines, int pawnLines)
    {
        int majorLines = totalLines - pawnLines;
        int center = Board.BoardSize / 2;

        // Major piece lines (from the back rank outward)
        for (int lineIdx = 0; lineIdx < majorLines; lineIdx++)
        {
            int rank = isWhite ? lineIdx : Board.BoardSize - 1 - lineIdx;

            if (lineIdx == 0)
            {
                // Back rank: King centered, symmetric pattern radiating outward
                board.SetPiece(new Vector2I(center, rank), new King(isWhite, new Vector2I(center, rank)));

                int[] pattern = { 1, 2, 3, 4 }; // Q, B, N, R
                for (int offset = 1; offset <= center + 1; offset++)
                {
                    int leftFile = center - offset;
                    int rightFile = center + offset;
                    int patternIdx = (offset - 1) % 4;

                    if (leftFile >= 0)
                    {
                        var pos = new Vector2I(leftFile, rank);
                        board.SetPiece(pos, CreatePieceByPattern(pattern[patternIdx], isWhite, pos));
                    }
                    if (rightFile < Board.BoardSize)
                    {
                        var pos = new Vector2I(rightFile, rank);
                        board.SetPiece(pos, CreatePieceByPattern(pattern[patternIdx], isWhite, pos));
                    }
                }
            }
            else
            {
                // Subsequent major piece lines – rotating patterns for variety
                int[] pattern = GetMajorLinePattern(lineIdx);
                for (int file = 0; file < Board.BoardSize; file++)
                {
                    var pos = new Vector2I(file, rank);
                    board.SetPiece(pos, CreatePieceByPattern(pattern[file % pattern.Length], isWhite, pos));
                }
            }
        }

        // Pawn lines (placed closest to center, after all major piece lines)
        for (int pawnLineIdx = 0; pawnLineIdx < pawnLines; pawnLineIdx++)
        {
            int rank = isWhite
                ? majorLines + pawnLineIdx
                : Board.BoardSize - 1 - majorLines - pawnLineIdx;

            for (int file = 0; file < Board.BoardSize; file++)
            {
                var pos = new Vector2I(file, rank);
                board.SetPiece(pos, new Pawn(isWhite, pos));
            }
        }
    }

    // Pattern for major piece lines beyond the King row (lineIdx >= 1)
    private static int[] GetMajorLinePattern(int lineIdx)
    {
        return ((lineIdx - 1) % 3) switch
        {
            0 => new int[] { 2, 3, 4, 1, 1, 4, 3, 2 }, // B, N, R, Q, Q, R, N, B
            1 => new int[] { 3, 2 },                    // N, B alternating
            2 => new int[] { 4, 1, 2, 3 },              // R, Q, B, N
            _ => new int[] { 1, 2, 3, 4 }
        };
    }

    private static void SetupLinesRandomized(Board board, int totalLines, int pawnLines)
    {
        var rng = new Random();
        SetupLinesSideRandomized(board, true, totalLines, pawnLines, rng);
        SetupLinesSideRandomized(board, false, totalLines, pawnLines, rng);
    }

    private static void SetupLinesSideRandomized(Board board, bool isWhite, int totalLines, int pawnLines, Random rng)
    {
        int majorLines = totalLines - pawnLines;
        int center = Board.BoardSize / 2;

        for (int lineIdx = 0; lineIdx < majorLines; lineIdx++)
        {
            int rank = isWhite ? lineIdx : Board.BoardSize - 1 - lineIdx;
            var pieces = new List<Piece>();

            if (lineIdx == 0)
            {
                pieces.Add(new King(isWhite, Vector2I.Zero));
                int[] pattern = { 1, 2, 3, 4 };
                for (int offset = 1; offset <= center + 1; offset++)
                {
                    int leftFile = center - offset;
                    int rightFile = center + offset;
                    int patternIdx = (offset - 1) % 4;
                    if (leftFile >= 0)
                        pieces.Add(CreatePieceByPattern(pattern[patternIdx], isWhite, Vector2I.Zero));
                    if (rightFile < Board.BoardSize)
                        pieces.Add(CreatePieceByPattern(pattern[patternIdx], isWhite, Vector2I.Zero));
                }
            }
            else
            {
                int[] pattern = GetMajorLinePattern(lineIdx);
                for (int file = 0; file < Board.BoardSize; file++)
                    pieces.Add(CreatePieceByPattern(pattern[file % pattern.Length], isWhite, Vector2I.Zero));
            }

            // Fisher-Yates shuffle
            for (int i = pieces.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pieces[i], pieces[j]) = (pieces[j], pieces[i]);
            }

            // Place shuffled pieces with correct positions
            for (int file = 0; file < Board.BoardSize && file < pieces.Count; file++)
            {
                var pos = new Vector2I(file, rank);
                pieces[file].Position = pos;
                board.SetPiece(pos, pieces[file]);
            }
        }

        // Pawn lines unchanged
        for (int pawnLineIdx = 0; pawnLineIdx < pawnLines; pawnLineIdx++)
        {
            int rank = isWhite
                ? majorLines + pawnLineIdx
                : Board.BoardSize - 1 - majorLines - pawnLineIdx;
            for (int file = 0; file < Board.BoardSize; file++)
            {
                var pos = new Vector2I(file, rank);
                board.SetPiece(pos, new Pawn(isWhite, pos));
            }
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
            SetupMode.Lines => "Lines",
            SetupMode.Custom => "Custom Setup",
            _ => "Unknown"
        };
    }

    public static string GetModeDescription(SetupMode mode)
    {
        if (mode == SetupMode.Custom)
            return "Create your own starting position!\nPlace pieces manually on the board.";

        int majorLines = TotalLines - PawnLines;
        string majorPart = majorLines == 1 ? "1 major piece line" : $"{majorLines} major piece lines";
        string pawnPart = PawnLines == 0 ? "no pawn lines"
                        : PawnLines == 1 ? "1 pawn line"
                        : $"{PawnLines} pawn lines";
        return $"{majorPart} + {pawnPart}.";
    }

    #endregion
}
