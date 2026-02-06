using Godot;
using System.Collections.Generic;

namespace GrandChess26;

public partial class SetupEditor : Control
{
    private Board _board;
    private PieceType? _selectedPieceType = null;
    private bool _selectedIsWhite = true;
    private bool _symmetryEnabled = true;

    private VBoxContainer _paletteContainer;
    private Label _validationLabel;
    private Button _startButton;
    private CheckButton _symmetryToggle;
    private Button _clearButton;
    private Label _instructionsLabel;

    // Piece buttons for selection
    private Dictionary<(PieceType, bool), Button> _pieceButtons = new();

    [Signal]
    public delegate void SetupCompleteEventHandler();

    [Signal]
    public delegate void SetupCancelledEventHandler();

    public void Initialize(Board board)
    {
        _board = board;
        CreateUI();
        ClearBoard();
    }

    private void CreateUI()
    {
        // Main container positioned on the right side
        var mainPanel = new PanelContainer();
        mainPanel.Name = "SetupPanel";
        mainPanel.Position = new Vector2(10, 180);
        mainPanel.CustomMinimumSize = new Vector2(280, 500);

        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color("#1a1a2e", 0.95f);
        styleBox.SetCornerRadiusAll(8);
        styleBox.SetContentMarginAll(10);
        mainPanel.AddThemeStyleboxOverride("panel", styleBox);
        AddChild(mainPanel);

        _paletteContainer = new VBoxContainer();
        _paletteContainer.Name = "PaletteContainer";
        mainPanel.AddChild(_paletteContainer);

        // Title
        var titleLabel = new Label();
        titleLabel.Text = "Custom Setup Mode";
        titleLabel.AddThemeFontSizeOverride("font_size", 16);
        titleLabel.AddThemeColorOverride("font_color", new Color("#ffffff"));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _paletteContainer.AddChild(titleLabel);

        _paletteContainer.AddChild(CreateSeparator());

        // Instructions
        _instructionsLabel = new Label();
        _instructionsLabel.Text = "Left-click: Place piece\nRight-click: Remove piece\nSelect piece type below:";
        _instructionsLabel.AddThemeFontSizeOverride("font_size", 11);
        _instructionsLabel.AddThemeColorOverride("font_color", new Color("#aaaaaa"));
        _paletteContainer.AddChild(_instructionsLabel);

        _paletteContainer.AddChild(CreateSeparator());

        // White pieces section
        var whiteLabel = new Label();
        whiteLabel.Text = "White Pieces:";
        whiteLabel.AddThemeFontSizeOverride("font_size", 12);
        whiteLabel.AddThemeColorOverride("font_color", new Color("#ffffff"));
        _paletteContainer.AddChild(whiteLabel);

        var whitePiecesContainer = new HBoxContainer();
        CreatePieceButtons(whitePiecesContainer, true);
        _paletteContainer.AddChild(whitePiecesContainer);

        _paletteContainer.AddChild(CreateSeparator());

        // Black pieces section
        var blackLabel = new Label();
        blackLabel.Text = "Black Pieces:";
        blackLabel.AddThemeFontSizeOverride("font_size", 12);
        blackLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        _paletteContainer.AddChild(blackLabel);

        var blackPiecesContainer = new HBoxContainer();
        CreatePieceButtons(blackPiecesContainer, false);
        _paletteContainer.AddChild(blackPiecesContainer);

        _paletteContainer.AddChild(CreateSeparator());

        // Symmetry toggle
        _symmetryToggle = new CheckButton();
        _symmetryToggle.Text = "Mirror placement";
        _symmetryToggle.ButtonPressed = true;
        _symmetryToggle.Toggled += OnSymmetryToggled;
        _symmetryToggle.AddThemeColorOverride("font_color", new Color("#ffffff"));
        _paletteContainer.AddChild(_symmetryToggle);

        // Helper text for symmetry
        var symmetryHelp = new Label();
        symmetryHelp.Text = "Automatically place mirrored\npieces when placing a piece.";
        symmetryHelp.AddThemeFontSizeOverride("font_size", 10);
        symmetryHelp.AddThemeColorOverride("font_color", new Color("#888888"));
        _paletteContainer.AddChild(symmetryHelp);

        _paletteContainer.AddChild(CreateSeparator());

        // Validation label
        _validationLabel = new Label();
        _validationLabel.Name = "ValidationLabel";
        _validationLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _validationLabel.CustomMinimumSize = new Vector2(260, 60);
        _validationLabel.AddThemeFontSizeOverride("font_size", 11);
        _paletteContainer.AddChild(_validationLabel);

        _paletteContainer.AddChild(CreateSeparator());

        // Button container
        var buttonContainer = new HBoxContainer();
        buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;

        // Clear button
        _clearButton = new Button();
        _clearButton.Text = "Clear All";
        _clearButton.CustomMinimumSize = new Vector2(80, 30);
        _clearButton.Pressed += OnClearPressed;
        buttonContainer.AddChild(_clearButton);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(10, 0);
        buttonContainer.AddChild(spacer);

        // Start button
        _startButton = new Button();
        _startButton.Text = "Start Game";
        _startButton.CustomMinimumSize = new Vector2(100, 30);
        _startButton.Pressed += OnStartPressed;
        buttonContainer.AddChild(_startButton);

        _paletteContainer.AddChild(buttonContainer);

        // Cancel button
        var cancelButton = new Button();
        cancelButton.Text = "Cancel";
        cancelButton.CustomMinimumSize = new Vector2(260, 25);
        cancelButton.Pressed += OnCancelPressed;
        _paletteContainer.AddChild(cancelButton);

        // Preset buttons
        _paletteContainer.AddChild(CreateSeparator());

        var presetLabel = new Label();
        presetLabel.Text = "Quick Presets:";
        presetLabel.AddThemeFontSizeOverride("font_size", 12);
        presetLabel.AddThemeColorOverride("font_color", new Color("#aaaaaa"));
        _paletteContainer.AddChild(presetLabel);

        var presetContainer = new HBoxContainer();

        var standardPreset = new Button();
        standardPreset.Text = "Standard";
        standardPreset.CustomMinimumSize = new Vector2(85, 25);
        standardPreset.Pressed += () => LoadPreset(SetupMode.TwoLines);
        presetContainer.AddChild(standardPreset);

        var minimalPreset = new Button();
        minimalPreset.Text = "Minimal";
        minimalPreset.CustomMinimumSize = new Vector2(85, 25);
        minimalPreset.Pressed += LoadMinimalPreset;
        presetContainer.AddChild(minimalPreset);

        var endgamePreset = new Button();
        endgamePreset.Text = "Endgame";
        endgamePreset.CustomMinimumSize = new Vector2(85, 25);
        endgamePreset.Pressed += LoadEndgamePreset;
        presetContainer.AddChild(endgamePreset);

        _paletteContainer.AddChild(presetContainer);

        UpdateValidation();
    }

    private HSeparator CreateSeparator()
    {
        var sep = new HSeparator();
        sep.CustomMinimumSize = new Vector2(0, 10);
        return sep;
    }

    private void CreatePieceButtons(HBoxContainer container, bool isWhite)
    {
        PieceType[] types = { PieceType.King, PieceType.Queen, PieceType.Rook,
                              PieceType.Bishop, PieceType.Knight, PieceType.Pawn };

        foreach (var type in types)
        {
            var button = new Button();
            button.CustomMinimumSize = new Vector2(40, 40);
            button.Text = GetPieceSymbol(type, isWhite);
            button.AddThemeFontSizeOverride("font_size", 24);

            button.Pressed += () => SelectPiece(type, isWhite);

            container.AddChild(button);
            _pieceButtons[(type, isWhite)] = button;
        }
    }

    private string GetPieceSymbol(PieceType type, bool isWhite)
    {
        return type switch
        {
            PieceType.King => isWhite ? "\u2654" : "\u265A",
            PieceType.Queen => isWhite ? "\u2655" : "\u265B",
            PieceType.Rook => isWhite ? "\u2656" : "\u265C",
            PieceType.Bishop => isWhite ? "\u2657" : "\u265D",
            PieceType.Knight => isWhite ? "\u2658" : "\u265E",
            PieceType.Pawn => isWhite ? "\u2659" : "\u265F",
            _ => "?"
        };
    }

    private void SelectPiece(PieceType type, bool isWhite)
    {
        _selectedPieceType = type;
        _selectedIsWhite = isWhite;

        // Update button visuals
        foreach (var kvp in _pieceButtons)
        {
            var btn = kvp.Value;
            if (kvp.Key == (type, isWhite))
            {
                btn.AddThemeColorOverride("font_color", new Color("#00ff00"));
            }
            else
            {
                btn.RemoveThemeColorOverride("font_color");
            }
        }
    }

    public void HandleBoardClick(Vector2I square, bool isRightClick)
    {
        if (isRightClick)
        {
            // Remove piece
            RemovePiece(square);
        }
        else if (_selectedPieceType.HasValue)
        {
            // Place piece
            PlacePiece(square, _selectedPieceType.Value, _selectedIsWhite);
        }

        UpdateValidation();
    }

    private void PlacePiece(Vector2I square, PieceType type, bool isWhite)
    {
        // Validate pawn placement (not on first or last rank)
        if (type == PieceType.Pawn)
        {
            if (square.Y == 0 || square.Y == Board.BoardSize - 1)
            {
                // Invalid pawn position
                return;
            }
        }

        // Create and place the piece
        Piece piece = CreatePiece(type, isWhite, square);
        _board.SetPiece(square, piece);

        // If symmetry is enabled, mirror the piece for the opposite color
        if (_symmetryEnabled)
        {
            Vector2I mirrorSquare = new Vector2I(square.X, Board.BoardSize - 1 - square.Y);

            // Don't place if there's already a same-color piece there
            var existingPiece = _board.GetPiece(mirrorSquare);
            if (existingPiece == null || existingPiece.IsWhite == isWhite)
            {
                // Validate pawn placement for mirror
                if (type == PieceType.Pawn && (mirrorSquare.Y == 0 || mirrorSquare.Y == Board.BoardSize - 1))
                {
                    return;
                }

                Piece mirrorPiece = CreatePiece(type, !isWhite, mirrorSquare);
                _board.SetPiece(mirrorSquare, mirrorPiece);
            }
        }
    }

    private void RemovePiece(Vector2I square)
    {
        var piece = _board.GetPiece(square);
        if (piece == null) return;

        bool wasWhite = piece.IsWhite;
        _board.SetPiece(square, null);

        // If symmetry enabled, also remove the mirror piece of opposite color
        if (_symmetryEnabled)
        {
            Vector2I mirrorSquare = new Vector2I(square.X, Board.BoardSize - 1 - square.Y);
            var mirrorPiece = _board.GetPiece(mirrorSquare);
            if (mirrorPiece != null && mirrorPiece.IsWhite != wasWhite)
            {
                _board.SetPiece(mirrorSquare, null);
            }
        }
    }

    private Piece CreatePiece(PieceType type, bool isWhite, Vector2I position)
    {
        return type switch
        {
            PieceType.King => new King(isWhite, position),
            PieceType.Queen => new Queen(isWhite, position),
            PieceType.Rook => new Rook(isWhite, position),
            PieceType.Bishop => new Bishop(isWhite, position),
            PieceType.Knight => new Knight(isWhite, position),
            PieceType.Pawn => new Pawn(isWhite, position),
            _ => new Pawn(isWhite, position)
        };
    }

    private void UpdateValidation()
    {
        var (isValid, errors) = ValidateSetup();

        if (isValid)
        {
            _validationLabel.Text = "Setup is valid!\nReady to start.";
            _validationLabel.AddThemeColorOverride("font_color", new Color("#00ff00"));
            _startButton.Disabled = false;
        }
        else
        {
            _validationLabel.Text = string.Join("\n", errors);
            _validationLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
            _startButton.Disabled = true;
        }
    }

    private (bool isValid, List<string> errors) ValidateSetup()
    {
        var errors = new List<string>();
        var board = _board.GetBoardState();

        int whiteKings = 0;
        int blackKings = 0;
        bool hasWhitePieces = false;
        bool hasBlackPieces = false;

        for (int file = 0; file < Board.BoardSize; file++)
        {
            for (int rank = 0; rank < Board.BoardSize; rank++)
            {
                var piece = board[file, rank];
                if (piece == null) continue;

                if (piece.IsWhite)
                    hasWhitePieces = true;
                else
                    hasBlackPieces = true;

                if (piece is King)
                {
                    if (piece.IsWhite)
                        whiteKings++;
                    else
                        blackKings++;
                }

                // Check pawns not on first/last rank (should be prevented, but double-check)
                if (piece is Pawn && (rank == 0 || rank == Board.BoardSize - 1))
                {
                    errors.Add($"Pawn on invalid rank at {Board.GetSquareNotation(new Vector2I(file, rank))}");
                }
            }
        }

        // Validate kings
        if (whiteKings == 0)
            errors.Add("White needs a King");
        else if (whiteKings > 1)
            errors.Add($"White has {whiteKings} Kings (need 1)");

        if (blackKings == 0)
            errors.Add("Black needs a King");
        else if (blackKings > 1)
            errors.Add($"Black has {blackKings} Kings (need 1)");

        // Validate both sides have pieces
        if (!hasWhitePieces)
            errors.Add("Place at least one white piece");
        if (!hasBlackPieces)
            errors.Add("Place at least one black piece");

        return (errors.Count == 0, errors);
    }

    private void ClearBoard()
    {
        _board.ClearBoard();
        UpdateValidation();
    }

    private void OnSymmetryToggled(bool pressed)
    {
        _symmetryEnabled = pressed;
    }

    private void OnClearPressed()
    {
        ClearBoard();
    }

    private void OnStartPressed()
    {
        var (isValid, _) = ValidateSetup();
        if (isValid)
        {
            EmitSignal(SignalName.SetupComplete);
        }
    }

    private void OnCancelPressed()
    {
        EmitSignal(SignalName.SetupCancelled);
    }

    private void LoadPreset(SetupMode mode)
    {
        SetupManager.SetupBoard(_board, mode);
        UpdateValidation();
    }

    private void LoadMinimalPreset()
    {
        _board.ClearBoard();

        int center = Board.BoardSize / 2;

        // Just kings and a few pieces
        _board.SetPiece(new Vector2I(center, 0), new King(true, new Vector2I(center, 0)));
        _board.SetPiece(new Vector2I(center, Board.BoardSize - 1), new King(false, new Vector2I(center, Board.BoardSize - 1)));

        // A couple of rooks each
        _board.SetPiece(new Vector2I(0, 0), new Rook(true, new Vector2I(0, 0)));
        _board.SetPiece(new Vector2I(Board.BoardSize - 1, 0), new Rook(true, new Vector2I(Board.BoardSize - 1, 0)));
        _board.SetPiece(new Vector2I(0, Board.BoardSize - 1), new Rook(false, new Vector2I(0, Board.BoardSize - 1)));
        _board.SetPiece(new Vector2I(Board.BoardSize - 1, Board.BoardSize - 1), new Rook(false, new Vector2I(Board.BoardSize - 1, Board.BoardSize - 1)));

        // One queen each
        _board.SetPiece(new Vector2I(center - 1, 0), new Queen(true, new Vector2I(center - 1, 0)));
        _board.SetPiece(new Vector2I(center - 1, Board.BoardSize - 1), new Queen(false, new Vector2I(center - 1, Board.BoardSize - 1)));

        UpdateValidation();
    }

    private void LoadEndgamePreset()
    {
        _board.ClearBoard();

        int center = Board.BoardSize / 2;

        // Kings
        _board.SetPiece(new Vector2I(center, 3), new King(true, new Vector2I(center, 3)));
        _board.SetPiece(new Vector2I(center, Board.BoardSize - 4), new King(false, new Vector2I(center, Board.BoardSize - 4)));

        // A few pawns each in the middle
        for (int i = -2; i <= 2; i++)
        {
            int file = center + i;
            if (file >= 0 && file < Board.BoardSize)
            {
                _board.SetPiece(new Vector2I(file, 6), new Pawn(true, new Vector2I(file, 6)));
                _board.SetPiece(new Vector2I(file, Board.BoardSize - 7), new Pawn(false, new Vector2I(file, Board.BoardSize - 7)));
            }
        }

        // One rook each
        _board.SetPiece(new Vector2I(2, 1), new Rook(true, new Vector2I(2, 1)));
        _board.SetPiece(new Vector2I(2, Board.BoardSize - 2), new Rook(false, new Vector2I(2, Board.BoardSize - 2)));

        UpdateValidation();
    }

    public new void Show()
    {
        Visible = true;
        ClearBoard();
        _selectedPieceType = null;

        // Reset button highlights
        foreach (var btn in _pieceButtons.Values)
        {
            btn.RemoveThemeColorOverride("font_color");
        }

        UpdateValidation();
        base.Show();
    }

    public new void Hide()
    {
        Visible = false;
        base.Hide();
    }
}
