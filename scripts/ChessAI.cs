using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GrandChess26;

public enum AIDifficulty
{
    Easy,      // Depth 1, some randomness
    Medium,    // Depth 2
    Hard       // Depth 3
}

public class ChessAI
{
    private readonly Random _random = new Random();

    // Piece values (centipawns)
    private const int PawnValue = 100;
    private const int KnightValue = 320;
    private const int BishopValue = 330;
    private const int RookValue = 500;
    private const int QueenValue = 900;
    private const int KingValue = 20000;

    // Search parameters
    private int _maxDepth;
    private bool _isWhite;
    private int _nodesSearched;

    public AIDifficulty Difficulty { get; set; } = AIDifficulty.Medium;

    public ChessAI(AIDifficulty difficulty = AIDifficulty.Medium)
    {
        Difficulty = difficulty;
    }

    public async Task<(Vector2I from, Vector2I to)?> GetBestMoveAsync(Board board, GameManager gameManager, bool isWhite)
    {
        return await Task.Run(() => GetBestMove(board, gameManager, isWhite));
    }

    public (Vector2I from, Vector2I to)? GetBestMove(Board board, GameManager gameManager, bool isWhite)
    {
        _isWhite = isWhite;
        _nodesSearched = 0;

        _maxDepth = Difficulty switch
        {
            AIDifficulty.Easy => 1,
            AIDifficulty.Medium => 2,
            AIDifficulty.Hard => 3,
            _ => 2
        };

        var boardState = board.GetBoardState();
        var allMoves = GetAllLegalMoves(boardState, gameManager, isWhite);

        if (allMoves.Count == 0)
            return null;

        // For easy mode, add some randomness
        if (Difficulty == AIDifficulty.Easy && _random.NextDouble() < 0.3)
        {
            return allMoves[_random.Next(allMoves.Count)];
        }

        // Order moves for better pruning
        allMoves = OrderMoves(allMoves, boardState);

        int bestScore = int.MinValue;
        var bestMoves = new List<(Vector2I from, Vector2I to)>();

        foreach (var move in allMoves)
        {
            var newState = SimulateMove(boardState, move.from, move.to);
            int score = -AlphaBeta(newState, gameManager, _maxDepth - 1, int.MinValue, int.MaxValue, !isWhite);

            if (score > bestScore)
            {
                bestScore = score;
                bestMoves.Clear();
                bestMoves.Add(move);
            }
            else if (score == bestScore)
            {
                bestMoves.Add(move);
            }
        }

        // Pick randomly among equally good moves
        if (bestMoves.Count > 0)
        {
            return bestMoves[_random.Next(bestMoves.Count)];
        }

        return allMoves[0];
    }

    private int AlphaBeta(Piece[,] board, GameManager gameManager, int depth, int alpha, int beta, bool isMaximizing)
    {
        _nodesSearched++;

        if (depth == 0)
        {
            return Evaluate(board, isMaximizing);
        }

        var moves = GetAllPossibleMoves(board, isMaximizing);

        if (moves.Count == 0)
        {
            // No moves - check if checkmate or stalemate
            if (IsKingInCheck(board, isMaximizing))
            {
                return isMaximizing ? -KingValue + depth : KingValue - depth; // Checkmate
            }
            return 0; // Stalemate
        }

        moves = OrderMoves(moves, board);

        if (isMaximizing)
        {
            int maxEval = int.MinValue;
            foreach (var move in moves)
            {
                var newState = SimulateMove(board, move.from, move.to);
                int eval = AlphaBeta(newState, gameManager, depth - 1, alpha, beta, false);
                maxEval = Math.Max(maxEval, eval);
                alpha = Math.Max(alpha, eval);
                if (beta <= alpha)
                    break;
            }
            return maxEval;
        }
        else
        {
            int minEval = int.MaxValue;
            foreach (var move in moves)
            {
                var newState = SimulateMove(board, move.from, move.to);
                int eval = AlphaBeta(newState, gameManager, depth - 1, alpha, beta, true);
                minEval = Math.Min(minEval, eval);
                beta = Math.Min(beta, eval);
                if (beta <= alpha)
                    break;
            }
            return minEval;
        }
    }

    private List<(Vector2I from, Vector2I to)> GetAllLegalMoves(Piece[,] board, GameManager gameManager, bool isWhite)
    {
        var moves = new List<(Vector2I from, Vector2I to)>();

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece != null && piece.IsWhite == isWhite)
                {
                    var from = new Vector2I(file, rank);
                    var legalMoves = gameManager.GetLegalMoves(from);
                    foreach (var to in legalMoves)
                    {
                        moves.Add((from, to));
                    }
                }
            }
        }

        return moves;
    }

    private List<(Vector2I from, Vector2I to)> GetAllPossibleMoves(Piece[,] board, bool isWhite)
    {
        var moves = new List<(Vector2I from, Vector2I to)>();

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece != null && piece.IsWhite == isWhite)
                {
                    var possibleMoves = piece.GetPossibleMoves(board);
                    foreach (var to in possibleMoves)
                    {
                        // Basic legality check - don't move into check
                        var simulated = SimulateMove(board, piece.Position, to);
                        if (!IsKingInCheck(simulated, isWhite))
                        {
                            moves.Add((piece.Position, to));
                        }
                    }
                }
            }
        }

        return moves;
    }

    private List<(Vector2I from, Vector2I to)> OrderMoves(List<(Vector2I from, Vector2I to)> moves, Piece[,] board)
    {
        // Order moves: captures first, then checks, then others
        return moves.OrderByDescending(m =>
        {
            int score = 0;
            var capturedPiece = board[m.to.X, m.to.Y];
            var movingPiece = board[m.from.X, m.from.Y];

            // Prioritize captures (MVV-LVA: Most Valuable Victim - Least Valuable Attacker)
            if (capturedPiece != null)
            {
                score += GetPieceValue(capturedPiece.Type) * 10 - GetPieceValue(movingPiece.Type);
            }

            // Prioritize center moves
            int centerDist = Math.Abs(m.to.X - Board.BoardSize / 2) + Math.Abs(m.to.Y - Board.BoardSize / 2);
            score -= centerDist;

            return score;
        }).ToList();
    }

    private Piece[,] SimulateMove(Piece[,] board, Vector2I from, Vector2I to)
    {
        var newBoard = new Piece[Board.BoardSize, Board.BoardSize];

        // Copy board
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece != null)
                {
                    newBoard[file, rank] = piece.Clone();
                }
            }
        }

        // Make move
        var movingPiece = newBoard[from.X, from.Y];
        if (movingPiece != null)
        {
            newBoard[to.X, to.Y] = movingPiece;
            newBoard[from.X, from.Y] = null;
            movingPiece.Position = to;
            movingPiece.HasMoved = true;

            // Handle pawn promotion
            if (movingPiece is Pawn)
            {
                int promotionRank = movingPiece.IsWhite ? Board.BoardSize - 1 : 0;
                if (to.Y == promotionRank)
                {
                    newBoard[to.X, to.Y] = new Queen(movingPiece.IsWhite, to);
                }
            }
        }

        return newBoard;
    }

    private bool IsKingInCheck(Piece[,] board, bool isWhite)
    {
        // Find king
        Vector2I? kingPos = null;
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece is King && piece.IsWhite == isWhite)
                {
                    kingPos = new Vector2I(file, rank);
                    break;
                }
            }
            if (kingPos.HasValue) break;
        }

        if (!kingPos.HasValue) return true; // No king = bad

        // Check if any enemy piece can attack the king
        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece != null && piece.IsWhite != isWhite)
                {
                    var moves = piece.GetPossibleMoves(board);
                    if (moves.Contains(kingPos.Value))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private int Evaluate(Piece[,] board, bool forWhite)
    {
        int score = 0;

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece == null) continue;

                int pieceScore = GetPieceValue(piece.Type);
                pieceScore += GetPositionalBonus(piece, file, rank);

                if (piece.IsWhite)
                    score += pieceScore;
                else
                    score -= pieceScore;
            }
        }

        // Return from the perspective of the player we're evaluating for
        return forWhite ? score : -score;
    }

    private int GetPieceValue(PieceType type)
    {
        return type switch
        {
            PieceType.Pawn => PawnValue,
            PieceType.Knight => KnightValue,
            PieceType.Bishop => BishopValue,
            PieceType.Rook => RookValue,
            PieceType.Queen => QueenValue,
            PieceType.King => KingValue,
            _ => 0
        };
    }

    private int GetPositionalBonus(Piece piece, int file, int rank)
    {
        int bonus = 0;
        int center = Board.BoardSize / 2;

        // Distance from center
        int fileDist = Math.Abs(file - center);
        int rankDist = Math.Abs(rank - center);
        int centerDist = fileDist + rankDist;

        switch (piece.Type)
        {
            case PieceType.Pawn:
                // Pawns are better advanced
                int advancement = piece.IsWhite ? rank : (Board.BoardSize - 1 - rank);
                bonus += advancement * 5;
                // Pawns in center are better
                bonus += (center - fileDist) * 2;
                break;

            case PieceType.Knight:
                // Knights are better in the center
                bonus += (Board.BoardSize - centerDist) * 3;
                // Knights are bad on the edge
                if (file == 0 || file == Board.BoardSize - 1 || rank == 0 || rank == Board.BoardSize - 1)
                    bonus -= 20;
                break;

            case PieceType.Bishop:
                // Bishops like long diagonals (center)
                bonus += (Board.BoardSize - centerDist) * 2;
                break;

            case PieceType.Rook:
                // Rooks on open files and 7th rank
                int seventhRank = piece.IsWhite ? Board.BoardSize - 2 : 1;
                if (rank == seventhRank)
                    bonus += 20;
                break;

            case PieceType.Queen:
                // Queen development but not too early
                if (!piece.HasMoved)
                    bonus -= 10; // Small penalty for unmoved queen
                bonus += (Board.BoardSize - centerDist); // Slight center preference
                break;

            case PieceType.King:
                // King safety - stay back in opening/midgame
                int homeRank = piece.IsWhite ? 0 : Board.BoardSize - 1;
                int distFromHome = Math.Abs(rank - homeRank);

                // Count material to determine game phase
                // In endgame, king should be active
                bonus -= distFromHome * 3; // Prefer king safety
                break;
        }

        return bonus;
    }

    public static string GetDifficultyName(AIDifficulty difficulty)
    {
        return difficulty switch
        {
            AIDifficulty.Easy => "Easy",
            AIDifficulty.Medium => "Medium",
            AIDifficulty.Hard => "Hard",
            _ => "Unknown"
        };
    }
}
