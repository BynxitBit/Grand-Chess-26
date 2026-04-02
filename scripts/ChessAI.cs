using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GrandChess26;

public enum AIDifficulty
{
    Easy,      // Depth 2, some randomness
    Medium,    // Depth 3
    Hard       // Depth 4
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
    private int _maxQDepth;
    private int _nodesSearched;
    private bool _searchAborted;
    private Stopwatch _stopwatch;
    private long _timeLimitMs;

    // Zobrist hashing
    private ulong[,,] _zobristTable;
    private ulong _zobristSideToMove;
    private int _zobristBoardSize;

    // Transposition table
    private TTEntry[] _tt;
    private int _ttSize;

    // Killer moves: [ply, slot]
    private (Vector2I from, Vector2I to)[,] _killerMoves;
    private const int MaxPly = 64;

    // Best move from previous iteration (for root move ordering)
    private (Vector2I from, Vector2I to)? _previousBestMove;

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
        _nodesSearched = 0;
        _searchAborted = false;
        int boardSize = Board.BoardSize;

        _maxDepth = Difficulty switch
        {
            AIDifficulty.Easy => 2,
            AIDifficulty.Medium => 3,
            AIDifficulty.Hard => 4,
            _ => 3
        };

        _maxQDepth = boardSize <= 28 ? 4 : 2;

        _timeLimitMs = Difficulty switch
        {
            AIDifficulty.Easy => 1000,
            AIDifficulty.Medium => 3000,
            AIDifficulty.Hard => 8000,
            _ => 3000
        };
        if (boardSize > 40)
            _timeLimitMs = (long)(_timeLimitMs * 1.5);

        InitZobrist(boardSize);

        _ttSize = boardSize > 28 ? 1048576 : 262144;
        _tt = new TTEntry[_ttSize];

        _killerMoves = new (Vector2I from, Vector2I to)[MaxPly, 2];
        _previousBestMove = null;

        var boardState = board.GetBoardState();
        var allMoves = GetAllLegalMoves(boardState, gameManager, isWhite);

        if (allMoves.Count == 0)
            return null;

        // For easy mode, 20% chance of random move
        if (Difficulty == AIDifficulty.Easy && _random.NextDouble() < 0.2)
        {
            return allMoves[_random.Next(allMoves.Count)];
        }

        // Iterative deepening
        _stopwatch = Stopwatch.StartNew();
        (Vector2I from, Vector2I to)? bestMove = allMoves[0];

        for (int depth = 1; depth <= _maxDepth; depth++)
        {
            int alpha = int.MinValue + 1;
            var moveScores = new List<(Vector2I from, Vector2I to, int score)>();
            var orderedMoves = OrderMovesRoot(allMoves, boardState);

            foreach (var move in orderedMoves)
            {
                var newState = SimulateMove(boardState, move.from, move.to);
                int score = -Negamax(newState, -int.MaxValue, -alpha, depth - 1, !isWhite, 1);

                if (_searchAborted)
                    break;

                moveScores.Add((move.from, move.to, score));
                if (score > alpha)
                    alpha = score;
            }

            if (!_searchAborted && moveScores.Count > 0)
            {
                // Track clean best for iterative deepening ordering
                var cleanBest = moveScores.MaxBy(ms => ms.score);
                _previousBestMove = (cleanBest.from, cleanBest.to);

                // For easy mode, add noise for selection
                if (Difficulty == AIDifficulty.Easy)
                {
                    for (int i = 0; i < moveScores.Count; i++)
                    {
                        var ms = moveScores[i];
                        moveScores[i] = (ms.from, ms.to, ms.score + _random.Next(-50, 51));
                    }
                }

                var best = moveScores.MaxBy(ms => ms.score);
                var equalMoves = moveScores.Where(ms => ms.score == best.score).ToList();
                var chosen = equalMoves[_random.Next(equalMoves.Count)];
                bestMove = (chosen.from, chosen.to);
            }

            if (_searchAborted || _stopwatch.ElapsedMilliseconds > _timeLimitMs)
                break;
        }

        return bestMove;
    }

    // --- Negamax Search ---

    private int Negamax(Piece[,] board, int alpha, int beta, int depth, bool sideToMove, int ply)
    {
        _nodesSearched++;
        if ((_nodesSearched & 4095) == 0 && _stopwatch.ElapsedMilliseconds > _timeLimitMs)
        {
            _searchAborted = true;
            return 0;
        }

        // Probe transposition table
        ulong hash = ComputeHash(board, sideToMove);
        int ttIndex = (int)(hash % (ulong)_ttSize);
        ref TTEntry ttEntry = ref _tt[ttIndex];
        (Vector2I from, Vector2I to)? ttBestMove = null;

        if (ttEntry.Hash == hash && ttEntry.Depth >= depth)
        {
            if (ttEntry.Flag == TTFlag.Exact)
                return ttEntry.Score;
            if (ttEntry.Flag == TTFlag.LowerBound && ttEntry.Score > alpha)
                alpha = ttEntry.Score;
            else if (ttEntry.Flag == TTFlag.UpperBound && ttEntry.Score < beta)
                beta = ttEntry.Score;
            if (alpha >= beta)
                return ttEntry.Score;
        }
        if (ttEntry.Hash == hash)
            ttBestMove = ttEntry.BestMove;

        // Quiescence at leaf
        if (depth <= 0)
        {
            return QuiescenceSearch(board, alpha, beta, sideToMove, _maxQDepth);
        }

        var moves = GetAllPossibleMoves(board, sideToMove);

        if (moves.Count == 0)
        {
            if (IsKingInCheck(board, sideToMove))
                return -(KingValue - ply); // Checkmate
            return 0; // Stalemate
        }

        moves = OrderMoves(moves, board, ply, ttBestMove);

        int origAlpha = alpha;
        (Vector2I from, Vector2I to)? localBestMove = null;
        int bestScore = int.MinValue + 1;

        foreach (var move in moves)
        {
            var newState = SimulateMove(board, move.from, move.to);
            int score = -Negamax(newState, -beta, -alpha, depth - 1, !sideToMove, ply + 1);

            if (_searchAborted)
                return 0;

            if (score > bestScore)
            {
                bestScore = score;
                localBestMove = move;
            }
            if (score > alpha)
                alpha = score;
            if (alpha >= beta)
            {
                // Killer move (non-capture only)
                if (board[move.to.X, move.to.Y] == null && ply < MaxPly)
                {
                    _killerMoves[ply, 1] = _killerMoves[ply, 0];
                    _killerMoves[ply, 0] = move;
                }
                break;
            }
        }

        // Store in TT
        TTFlag flag;
        if (bestScore <= origAlpha)
            flag = TTFlag.UpperBound;
        else if (bestScore >= beta)
            flag = TTFlag.LowerBound;
        else
            flag = TTFlag.Exact;

        _tt[ttIndex] = new TTEntry
        {
            Hash = hash,
            Depth = depth,
            Score = bestScore,
            Flag = flag,
            BestMove = localBestMove
        };

        return bestScore;
    }

    // --- Quiescence Search ---

    private int QuiescenceSearch(Piece[,] board, int alpha, int beta, bool sideToMove, int qDepthLeft)
    {
        _nodesSearched++;
        if ((_nodesSearched & 4095) == 0 && _stopwatch.ElapsedMilliseconds > _timeLimitMs)
        {
            _searchAborted = true;
            return 0;
        }

        int standPat = EvaluateForSide(board, sideToMove);

        if (qDepthLeft <= 0)
            return standPat;

        if (standPat >= beta)
            return beta;

        // Delta pruning
        if (standPat + QueenValue + 200 < alpha)
            return alpha;

        if (standPat > alpha)
            alpha = standPat;

        var captureMoves = GetCaptureMoves(board, sideToMove);
        captureMoves = OrderMoves(captureMoves, board, 0, null);

        foreach (var move in captureMoves)
        {
            var newState = SimulateMove(board, move.from, move.to);

            // Legality check (captures weren't pre-filtered for legality)
            if (IsKingInCheck(newState, sideToMove))
                continue;

            int score = -QuiescenceSearch(newState, -beta, -alpha, !sideToMove, qDepthLeft - 1);

            if (_searchAborted)
                return 0;

            if (score >= beta)
                return beta;
            if (score > alpha)
                alpha = score;
        }

        return alpha;
    }

    // --- Move Generation ---

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

    private List<(Vector2I from, Vector2I to)> GetCaptureMoves(Piece[,] board, bool isWhite)
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
                        if (board[to.X, to.Y] != null && board[to.X, to.Y].IsWhite != isWhite)
                        {
                            moves.Add((piece.Position, to));
                        }
                    }
                }
            }
        }

        return moves;
    }

    // --- Move Ordering ---

    private List<(Vector2I from, Vector2I to)> OrderMovesRoot(List<(Vector2I from, Vector2I to)> moves, Piece[,] board)
    {
        return moves.OrderByDescending(m =>
        {
            int score = 0;

            if (_previousBestMove.HasValue && m.from == _previousBestMove.Value.from && m.to == _previousBestMove.Value.to)
                score += 100000;

            var capturedPiece = board[m.to.X, m.to.Y];
            var movingPiece = board[m.from.X, m.from.Y];

            if (capturedPiece != null)
                score += 10000 + GetPieceValue(capturedPiece.Type) * 10 - GetPieceValue(movingPiece.Type);

            int centerDist = Math.Abs(m.to.X - Board.BoardSize / 2) + Math.Abs(m.to.Y - Board.BoardSize / 2);
            score -= centerDist;

            return score;
        }).ToList();
    }

    private List<(Vector2I from, Vector2I to)> OrderMoves(List<(Vector2I from, Vector2I to)> moves, Piece[,] board, int ply, (Vector2I from, Vector2I to)? ttBestMove)
    {
        return moves.OrderByDescending(m =>
        {
            int score = 0;

            // TT best move
            if (ttBestMove.HasValue && m.from == ttBestMove.Value.from && m.to == ttBestMove.Value.to)
                return 100000;

            var capturedPiece = board[m.to.X, m.to.Y];
            var movingPiece = board[m.from.X, m.from.Y];

            // MVV-LVA for captures
            if (capturedPiece != null)
                score += 10000 + GetPieceValue(capturedPiece.Type) * 10 - GetPieceValue(movingPiece.Type);

            // Killer moves
            if (ply < MaxPly)
            {
                if (m.from == _killerMoves[ply, 0].from && m.to == _killerMoves[ply, 0].to)
                    score += 5000;
                else if (m.from == _killerMoves[ply, 1].from && m.to == _killerMoves[ply, 1].to)
                    score += 4900;
            }

            // Center preference
            int centerDist = Math.Abs(m.to.X - Board.BoardSize / 2) + Math.Abs(m.to.Y - Board.BoardSize / 2);
            score -= centerDist;

            return score;
        }).ToList();
    }

    // --- Simulation ---

    private Piece[,] SimulateMove(Piece[,] board, Vector2I from, Vector2I to)
    {
        var newBoard = new Piece[Board.BoardSize, Board.BoardSize];

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

        if (!kingPos.HasValue) return true;

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

    // --- Evaluation ---

    private int EvaluateForSide(Piece[,] board, bool sideToMove)
    {
        int whiteScore = 0;
        int blackScore = 0;
        int size = Board.BoardSize;

        // Count non-pawn, non-king material for game phase detection
        int whiteMaterial = 0;
        int blackMaterial = 0;
        int whiteBishops = 0;
        int blackBishops = 0;

        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece == null) continue;

                int val = GetPieceValue(piece.Type);
                if (piece.Type != PieceType.King && piece.Type != PieceType.Pawn)
                {
                    if (piece.IsWhite) whiteMaterial += val;
                    else blackMaterial += val;
                }
                if (piece.Type == PieceType.Bishop)
                {
                    if (piece.IsWhite) whiteBishops++;
                    else blackBishops++;
                }
            }
        }

        // Game phase: 1.0 = opening/middlegame, 0.0 = endgame
        int totalMaterial = whiteMaterial + blackMaterial;
        int estimatedStartMaterial = Math.Max(3200, totalMaterial + 1000);
        float gamePhase = Math.Clamp((float)totalMaterial / estimatedStartMaterial, 0f, 1f);

        // Full evaluation
        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece == null) continue;

                int pieceScore = GetPieceValue(piece.Type);
                pieceScore += GetPositionalBonus(piece, f, r, board, gamePhase);

                if (piece.IsWhite)
                    whiteScore += pieceScore;
                else
                    blackScore += pieceScore;
            }
        }

        // Bishop pair bonus
        if (whiteBishops >= 2) whiteScore += 30;
        if (blackBishops >= 2) blackScore += 30;

        // Pawn structure
        whiteScore += EvaluatePawnStructure(board, true);
        blackScore += EvaluatePawnStructure(board, false);

        // Rook on open/semi-open files
        whiteScore += EvaluateRooks(board, true);
        blackScore += EvaluateRooks(board, false);

        int score = whiteScore - blackScore;
        return sideToMove ? score : -score;
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
            PieceType.Archbishop => 650,   // Bishop + Knight
            PieceType.Chancellor => 820,   // Rook + Knight
            PieceType.Nightrider => 500,
            PieceType.Cannon => 450,
            PieceType.Camel => 200,
            _ => 0
        };
    }

    private int GetPositionalBonus(Piece piece, int file, int rank, Piece[,] board, float gamePhase)
    {
        int bonus = 0;
        int size = Board.BoardSize;
        int center = size / 2;
        int fileDist = Math.Abs(file - center);
        int rankDist = Math.Abs(rank - center);
        int centerDist = fileDist + rankDist;

        switch (piece.Type)
        {
            case PieceType.Pawn:
                int advancement = piece.IsWhite ? rank : (size - 1 - rank);
                bonus += advancement * 5;
                bonus += (center - fileDist) * 2;
                break;

            case PieceType.Knight:
                bonus += (size - centerDist) * 3;
                if (file == 0 || file == size - 1 || rank == 0 || rank == size - 1)
                    bonus -= 20;
                break;

            case PieceType.Bishop:
                bonus += (size - centerDist) * 2;
                break;

            case PieceType.Rook:
                int seventhRank = piece.IsWhite ? size - 2 : 1;
                if (rank == seventhRank)
                    bonus += 20;
                break;

            case PieceType.Queen:
                if (!piece.HasMoved)
                    bonus -= 10;
                bonus += (size - centerDist);
                break;

            case PieceType.King:
                int homeRank = piece.IsWhite ? 0 : size - 1;
                int distFromHome = Math.Abs(rank - homeRank);

                // Middlegame: king stays back, bonus for castled (flank) position
                int middlegameBonus = -distFromHome * 5;
                if (fileDist > center / 2)
                    middlegameBonus += 15;

                // Endgame: king centralizes
                int endgameBonus = (size - centerDist) * 3;

                bonus += (int)(middlegameBonus * gamePhase + endgameBonus * (1f - gamePhase));
                break;
        }

        return bonus;
    }

    private int EvaluatePawnStructure(Piece[,] board, bool isWhite)
    {
        int score = 0;
        int size = Board.BoardSize;

        int[] pawnCountOnFile = new int[size];
        bool[] hasPawnOnFile = new bool[size];

        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece != null && piece.Type == PieceType.Pawn && piece.IsWhite == isWhite)
                {
                    pawnCountOnFile[f]++;
                    hasPawnOnFile[f] = true;
                }
            }
        }

        for (int f = 0; f < size; f++)
        {
            // Doubled pawn penalty
            if (pawnCountOnFile[f] > 1)
                score -= 15 * (pawnCountOnFile[f] - 1);

            // Isolated pawn penalty
            if (hasPawnOnFile[f])
            {
                bool hasNeighbor = (f > 0 && hasPawnOnFile[f - 1]) || (f < size - 1 && hasPawnOnFile[f + 1]);
                if (!hasNeighbor)
                    score -= 12 * pawnCountOnFile[f];
            }
        }

        // Passed pawn bonus
        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece == null || piece.Type != PieceType.Pawn || piece.IsWhite != isWhite)
                    continue;

                if (IsPassedPawn(board, f, r, isWhite))
                {
                    int distToPromotion = isWhite ? (size - 1 - r) : r;
                    score += 20 + (size - 1 - distToPromotion) * 10;
                }
            }
        }

        return score;
    }

    private bool IsPassedPawn(Piece[,] board, int file, int rank, bool isWhite)
    {
        int size = Board.BoardSize;

        for (int f = Math.Max(0, file - 1); f <= Math.Min(size - 1, file + 1); f++)
        {
            if (isWhite)
            {
                for (int r = rank + 1; r < size; r++)
                {
                    var piece = board[f, r];
                    if (piece != null && piece.Type == PieceType.Pawn && !piece.IsWhite)
                        return false;
                }
            }
            else
            {
                for (int r = rank - 1; r >= 0; r--)
                {
                    var piece = board[f, r];
                    if (piece != null && piece.Type == PieceType.Pawn && piece.IsWhite)
                        return false;
                }
            }
        }

        return true;
    }

    private int EvaluateRooks(Piece[,] board, bool isWhite)
    {
        int score = 0;
        int size = Board.BoardSize;

        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece == null || piece.Type != PieceType.Rook || piece.IsWhite != isWhite)
                    continue;

                bool hasOwnPawn = false;
                bool hasEnemyPawn = false;

                for (int checkR = 0; checkR < size; checkR++)
                {
                    var p = board[f, checkR];
                    if (p != null && p.Type == PieceType.Pawn)
                    {
                        if (p.IsWhite == isWhite) hasOwnPawn = true;
                        else hasEnemyPawn = true;
                    }
                }

                if (!hasOwnPawn && !hasEnemyPawn)
                    score += 20; // Open file
                else if (!hasOwnPawn)
                    score += 10; // Semi-open file
            }
        }

        return score;
    }

    // --- Zobrist Hashing ---

    private void InitZobrist(int boardSize)
    {
        if (_zobristBoardSize == boardSize && _zobristTable != null)
            return;

        _zobristBoardSize = boardSize;
        _zobristTable = new ulong[boardSize, boardSize, 12];
        var rng = new Random(0x12345678); // Fixed seed for determinism

        for (int f = 0; f < boardSize; f++)
            for (int r = 0; r < boardSize; r++)
                for (int p = 0; p < 12; p++)
                    _zobristTable[f, r, p] = NextUlong(rng);

        _zobristSideToMove = NextUlong(rng);
    }

    private static ulong NextUlong(Random rng)
    {
        byte[] buf = new byte[8];
        rng.NextBytes(buf);
        return BitConverter.ToUInt64(buf, 0);
    }

    private int PieceToZobristIndex(Piece piece)
    {
        int typeIndex = piece.Type switch
        {
            PieceType.King => 0,
            PieceType.Queen => 1,
            PieceType.Rook => 2,
            PieceType.Bishop => 3,
            PieceType.Knight => 4,
            PieceType.Pawn => 5,
            _ => 0
        };
        return typeIndex * 2 + (piece.IsWhite ? 0 : 1);
    }

    private ulong ComputeHash(Piece[,] board, bool sideToMove)
    {
        ulong hash = 0;
        int size = Board.BoardSize;

        for (int f = 0; f < size; f++)
        {
            for (int r = 0; r < size; r++)
            {
                var piece = board[f, r];
                if (piece != null)
                    hash ^= _zobristTable[f, r, PieceToZobristIndex(piece)];
            }
        }

        if (sideToMove)
            hash ^= _zobristSideToMove;

        return hash;
    }

    // --- Transposition Table ---

    private enum TTFlag : byte
    {
        Exact,
        LowerBound,
        UpperBound
    }

    private struct TTEntry
    {
        public ulong Hash;
        public int Depth;
        public int Score;
        public TTFlag Flag;
        public (Vector2I from, Vector2I to)? BestMove;
    }

    // --- Utility ---

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
