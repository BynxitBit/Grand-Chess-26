using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public partial class Board : Node2D
{
    public static int BoardSize { get; private set; } = 26;
    public const int MinBoardSize = 3;
    public const int MaxBoardSize = 99;
    public const float SquareSize = 32f;

    private Color _lightSquare = new Color("#f0d9b5");
    private Color _darkSquare = new Color("#b58863");
    private Color _highlightColor = new Color("#829769", 0.8f);
    private Color _selectedColor = new Color("#646f40", 0.9f);
    private Color _checkColor = new Color("#ff0000", 0.5f);

    private Piece[,] _pieces;
    private Dictionary<string, Texture2D> _pieceTextures = new Dictionary<string, Texture2D>();
    private string _pieceStyle = "outline";

    public static readonly string[] PieceStyles = { "outline", "solid", "wood-outline", "wood", "flat-style" };
    public static readonly string[] PieceStyleNames = { "Outline", "Solid", "Wood Outline", "Wood", "Flat" };

    public Board()
    {
        _pieces = new Piece[BoardSize, BoardSize];
    }

    public void SetBoardSize(int size)
    {
        size = Math.Clamp(size, MinBoardSize, MaxBoardSize);
        if (size != BoardSize)
        {
            BoardSize = size;
            _pieces = new Piece[BoardSize, BoardSize];
            _selectedSquare = null;
            _legalMoves.Clear();
            _kingInCheck = null;
            QueueRedraw();
        }
    }
    private Vector2I? _selectedSquare = null;
    private List<Vector2I> _legalMoves = new List<Vector2I>();
    private Vector2I? _kingInCheck = null;
    private bool _isFlipped = false;

    public bool IsFlipped => _isFlipped;

    public void FlipBoard()
    {
        _isFlipped = !_isFlipped;
        QueueRedraw();
    }

    public void SetFlipped(bool flipped)
    {
        _isFlipped = flipped;
        QueueRedraw();
    }

    [Signal]
    public delegate void PieceMoveRequestedEventHandler(Vector2I from, Vector2I to);

    [Signal]
    public delegate void SquareClickedEventHandler(Vector2I square);

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawBoard();
        DrawHighlights();
        DrawPieces();
    }

    private void DrawBoard()
    {
        for (int file = 0; file < BoardSize; file++)
        {
            for (int rank = 0; rank < BoardSize; rank++)
            {
                bool isLight = (file + rank) % 2 == 0;
                Color color = isLight ? _lightSquare : _darkSquare;

                Vector2 pos = new Vector2(file * SquareSize, (BoardSize - 1 - rank) * SquareSize);
                DrawRect(new Rect2(pos, new Vector2(SquareSize, SquareSize)), color);
            }
        }

        // Draw border
        DrawRect(new Rect2(Vector2.Zero, new Vector2(BoardSize * SquareSize, BoardSize * SquareSize)),
                 new Color("#333333"), false, 2f);

        // Draw coordinates
        DrawCoordinates();
    }

    private void DrawCoordinates()
    {
        var font = ThemeDB.FallbackFont;
        int fontSize = 10;

        for (int i = 0; i < BoardSize; i++)
        {
            // File labels (a-z, aa, ab)
            int fileIndex = _isFlipped ? (BoardSize - 1 - i) : i;
            string fileLabel = GetFileLabel(fileIndex);
            Vector2 filePos = new Vector2(i * SquareSize + SquareSize / 2 - 4, BoardSize * SquareSize + 12);
            DrawString(font, filePos, fileLabel, HorizontalAlignment.Center, -1, fontSize, new Color("#666666"));

            // Rank labels (1-28)
            int rankIndex = _isFlipped ? i : (BoardSize - 1 - i);
            string rankLabel = (rankIndex + 1).ToString();
            Vector2 rankPos = new Vector2(-16, i * SquareSize + SquareSize / 2 + 4);
            DrawString(font, rankPos, rankLabel, HorizontalAlignment.Right, -1, fontSize, new Color("#666666"));
        }
    }

    private void DrawHighlights()
    {
        // Draw check highlight
        if (_kingInCheck.HasValue)
        {
            Vector2 checkPos = SquareToPixel(_kingInCheck.Value);
            DrawRect(new Rect2(checkPos, new Vector2(SquareSize, SquareSize)), _checkColor);
        }

        // Draw selected square
        if (_selectedSquare.HasValue)
        {
            Vector2 selectedPos = SquareToPixel(_selectedSquare.Value);
            DrawRect(new Rect2(selectedPos, new Vector2(SquareSize, SquareSize)), _selectedColor);
        }

        // Draw legal move highlights
        foreach (var move in _legalMoves)
        {
            Vector2 movePos = SquareToPixel(move);

            // Check if there's a piece to capture
            if (_pieces[move.X, move.Y] != null)
            {
                // Draw corner triangles for captures
                DrawRect(new Rect2(movePos, new Vector2(SquareSize, SquareSize)), _highlightColor);
            }
            else
            {
                // Draw circle for empty square moves
                Vector2 center = movePos + new Vector2(SquareSize / 2, SquareSize / 2);
                DrawCircle(center, SquareSize / 6, _highlightColor);
            }
        }
    }

    public void SetPieceStyle(string style)
    {
        if (_pieceStyle != style)
        {
            _pieceStyle = style;
            _pieceTextures.Clear();
            QueueRedraw();
        }
    }

    public string GetPieceStyle() => _pieceStyle;

    private Texture2D GetPieceTexture(Piece piece)
    {
        string colorCode = piece.IsWhite ? "w" : "b";
        string typeName = piece.Type switch
        {
            PieceType.King => "king",
            PieceType.Queen => "queen",
            PieceType.Rook => "rook",
            PieceType.Bishop => "bishop",
            PieceType.Knight => "knight",
            PieceType.Pawn => "pawn",
            _ => "pawn"
        };
        string key = $"{_pieceStyle}/{typeName}-{colorCode}";

        if (!_pieceTextures.TryGetValue(key, out Texture2D texture))
        {
            texture = GD.Load<Texture2D>($"res://assets/pieces/{key}.svg");
            _pieceTextures[key] = texture;
        }

        return texture;
    }

    private void DrawPieces()
    {
        for (int file = 0; file < BoardSize; file++)
        {
            for (int rank = 0; rank < BoardSize; rank++)
            {
                Piece piece = _pieces[file, rank];
                if (piece != null)
                {
                    Texture2D texture = GetPieceTexture(piece);
                    if (texture != null)
                    {
                        Vector2 squarePos = SquareToPixel(new Vector2I(file, rank));
                        Rect2 destRect = new Rect2(squarePos, new Vector2(SquareSize, SquareSize));
                        DrawTextureRect(texture, destRect, false);
                    }
                }
            }
        }
    }

    public Vector2 SquareToPixel(Vector2I square)
    {
        if (_isFlipped)
        {
            return new Vector2((BoardSize - 1 - square.X) * SquareSize, square.Y * SquareSize);
        }
        return new Vector2(square.X * SquareSize, (BoardSize - 1 - square.Y) * SquareSize);
    }

    public Vector2I? PixelToSquare(Vector2 pixel)
    {
        int pixelFile = (int)(pixel.X / SquareSize);
        int pixelRank = (int)(pixel.Y / SquareSize);

        int file, rank;
        if (_isFlipped)
        {
            file = BoardSize - 1 - pixelFile;
            rank = pixelRank;
        }
        else
        {
            file = pixelFile;
            rank = BoardSize - 1 - pixelRank;
        }

        if (file >= 0 && file < BoardSize && rank >= 0 && rank < BoardSize)
        {
            return new Vector2I(file, rank);
        }
        return null;
    }

    public static string GetFileLabel(int file)
    {
        if (file < 26)
        {
            return ((char)('a' + file)).ToString();
        }
        else
        {
            return "a" + ((char)('a' + file - 26)).ToString();
        }
    }

    public static string GetSquareNotation(Vector2I square)
    {
        return GetFileLabel(square.X) + (square.Y + 1).ToString();
    }

    public void SetPiece(Vector2I square, Piece piece)
    {
        _pieces[square.X, square.Y] = piece;
        if (piece != null)
        {
            piece.Position = square;
        }
        QueueRedraw();
    }

    public Piece GetPiece(Vector2I square)
    {
        if (square.X < 0 || square.X >= BoardSize || square.Y < 0 || square.Y >= BoardSize)
        {
            return null;
        }
        return _pieces[square.X, square.Y];
    }

    public void MovePiece(Vector2I from, Vector2I to)
    {
        Piece piece = _pieces[from.X, from.Y];
        if (piece != null)
        {
            _pieces[from.X, from.Y] = null;
            _pieces[to.X, to.Y] = piece;
            piece.Position = to;
            piece.HasMoved = true;
        }
        QueueRedraw();
    }

    public void SetSelectedSquare(Vector2I? square)
    {
        _selectedSquare = square;
        QueueRedraw();
    }

    public void SetLegalMoves(List<Vector2I> moves)
    {
        _legalMoves = moves ?? new List<Vector2I>();
        QueueRedraw();
    }

    public void SetKingInCheck(Vector2I? kingPos)
    {
        _kingInCheck = kingPos;
        QueueRedraw();
    }

    public void ClearHighlights()
    {
        _selectedSquare = null;
        _legalMoves.Clear();
        QueueRedraw();
    }

    public Piece[,] GetBoardState()
    {
        return _pieces;
    }

    public void ClearBoard()
    {
        _pieces = new Piece[BoardSize, BoardSize];
        _selectedSquare = null;
        _legalMoves.Clear();
        _kingInCheck = null;
        QueueRedraw();
    }

    public float GetBoardPixelSize()
    {
        return BoardSize * SquareSize;
    }
}
