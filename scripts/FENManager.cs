using Godot;
using System;
using System.Text;

namespace GrandChess26;

public static class FENManager
{
    // Extended FEN format for variable board sizes:
    // [board_size]:[piece_placement] [turn] [castling] [en_passant] [halfmove] [fullmove]
    // Example for 26x26: "26:rnbq...kbnr/pppp.../... w KQkq - 0 1"

    public static string ExportFEN(Board board, GameManager gameManager)
    {
        var sb = new StringBuilder();
        int size = Board.BoardSize;
        var boardState = board.GetBoardState();

        // Board size prefix
        sb.Append(size);
        sb.Append(':');

        // Piece placement (from top rank to bottom)
        for (int rank = size - 1; rank >= 0; rank--)
        {
            int emptyCount = 0;

            for (int file = 0; file < size; file++)
            {
                Piece piece = boardState[file, rank];

                if (piece == null)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        sb.Append(emptyCount);
                        emptyCount = 0;
                    }
                    sb.Append(PieceToFEN(piece));
                }
            }

            if (emptyCount > 0)
            {
                sb.Append(emptyCount);
            }

            if (rank > 0)
            {
                sb.Append('/');
            }
        }

        // Turn
        sb.Append(' ');
        sb.Append(gameManager.IsWhiteTurn ? 'w' : 'b');

        // Castling rights (simplified - check if kings/rooks have moved)
        sb.Append(' ');
        string castling = GetCastlingRights(boardState, size);
        sb.Append(string.IsNullOrEmpty(castling) ? "-" : castling);

        // En passant (simplified - not tracking in FEN for now)
        sb.Append(" -");

        // Halfmove clock and fullmove number (simplified)
        sb.Append(" 0 1");

        return sb.ToString();
    }

    public static (bool success, string error) ImportFEN(string fen, Board board, GameManager gameManager)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return (false, "FEN string is empty");
        }

        try
        {
            string[] parts = fen.Trim().Split(' ');
            if (parts.Length < 1)
            {
                return (false, "Invalid FEN format");
            }

            string piecePlacement = parts[0];
            int boardSize;

            // Check for board size prefix
            if (piecePlacement.Contains(':'))
            {
                string[] sizeParts = piecePlacement.Split(':');
                if (!int.TryParse(sizeParts[0], out boardSize))
                {
                    return (false, "Invalid board size in FEN");
                }
                piecePlacement = sizeParts[1];
            }
            else
            {
                // Standard 8x8 FEN
                boardSize = 8;
            }

            // Validate and set board size
            if (boardSize < Board.MinBoardSize || boardSize > Board.MaxBoardSize)
            {
                return (false, $"Board size must be between {Board.MinBoardSize} and {Board.MaxBoardSize}");
            }

            board.SetBoardSize(boardSize);
            board.ClearBoard();

            // Parse piece placement
            string[] ranks = piecePlacement.Split('/');
            if (ranks.Length != boardSize)
            {
                return (false, $"Expected {boardSize} ranks, got {ranks.Length}");
            }

            for (int rankIdx = 0; rankIdx < ranks.Length; rankIdx++)
            {
                int rank = boardSize - 1 - rankIdx; // FEN starts from top rank
                int file = 0;
                string rankData = ranks[rankIdx];

                int i = 0;
                while (i < rankData.Length && file < boardSize)
                {
                    char c = rankData[i];

                    if (char.IsDigit(c))
                    {
                        // Parse number (can be multi-digit for large boards)
                        int numStart = i;
                        while (i < rankData.Length && char.IsDigit(rankData[i]))
                        {
                            i++;
                        }
                        int emptySquares = int.Parse(rankData.Substring(numStart, i - numStart));
                        file += emptySquares;
                    }
                    else
                    {
                        // Piece character
                        Piece piece = FENToPiece(c, new Vector2I(file, rank));
                        if (piece != null)
                        {
                            board.SetPiece(new Vector2I(file, rank), piece);
                        }
                        file++;
                        i++;
                    }
                }
            }

            // Parse turn (if provided)
            bool isWhiteTurn = true;
            if (parts.Length >= 2)
            {
                isWhiteTurn = parts[1].ToLower() != "b";
            }

            // Reset game state
            gameManager.ResetTurnState();
            if (!isWhiteTurn)
            {
                // If black's turn, we need to set this up
                gameManager.SetTurn(false);
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"Error parsing FEN: {ex.Message}");
        }
    }

    private static char PieceToFEN(Piece piece)
    {
        char c = piece.Type switch
        {
            PieceType.King => 'k',
            PieceType.Queen => 'q',
            PieceType.Rook => 'r',
            PieceType.Bishop => 'b',
            PieceType.Knight => 'n',
            PieceType.Pawn => 'p',
            _ => '?'
        };

        return piece.IsWhite ? char.ToUpper(c) : c;
    }

    private static Piece FENToPiece(char c, Vector2I position)
    {
        bool isWhite = char.IsUpper(c);
        char lower = char.ToLower(c);

        return lower switch
        {
            'k' => new King(isWhite, position),
            'q' => new Queen(isWhite, position),
            'r' => new Rook(isWhite, position),
            'b' => new Bishop(isWhite, position),
            'n' => new Knight(isWhite, position),
            'p' => new Pawn(isWhite, position),
            _ => null
        };
    }

    private static string GetCastlingRights(Piece[,] board, int size)
    {
        var sb = new StringBuilder();

        // Find white king and check castling rights
        for (int file = 0; file < size; file++)
        {
            Piece piece = board[file, 0];
            if (piece is King && piece.IsWhite && !piece.HasMoved)
            {
                // Check for kingside rook
                for (int f = file + 1; f < size; f++)
                {
                    Piece r = board[f, 0];
                    if (r is Rook && r.IsWhite && !r.HasMoved)
                    {
                        sb.Append('K');
                        break;
                    }
                }
                // Check for queenside rook
                for (int f = file - 1; f >= 0; f--)
                {
                    Piece r = board[f, 0];
                    if (r is Rook && r.IsWhite && !r.HasMoved)
                    {
                        sb.Append('Q');
                        break;
                    }
                }
                break;
            }
        }

        // Find black king and check castling rights
        for (int file = 0; file < size; file++)
        {
            Piece piece = board[file, size - 1];
            if (piece is King && !piece.IsWhite && !piece.HasMoved)
            {
                // Check for kingside rook
                for (int f = file + 1; f < size; f++)
                {
                    Piece r = board[f, size - 1];
                    if (r is Rook && !r.IsWhite && !r.HasMoved)
                    {
                        sb.Append('k');
                        break;
                    }
                }
                // Check for queenside rook
                for (int f = file - 1; f >= 0; f--)
                {
                    Piece r = board[f, size - 1];
                    if (r is Rook && !r.IsWhite && !r.HasMoved)
                    {
                        sb.Append('q');
                        break;
                    }
                }
                break;
            }
        }

        return sb.ToString();
    }
}
