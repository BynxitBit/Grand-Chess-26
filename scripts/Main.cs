using Godot;
using System;
using System.Collections.Generic;

namespace GrandChess26;

public enum GameType
{
    PvP,
    PvE,
    Multiplayer
}

public partial class Main : Node2D
{
    private Board _board;
    private GameManager _gameManager;
    private Camera2D _camera;
    private Label _statusLabel;
    private Label _modeDescLabel;
    private OptionButton _modeSelector;
    private OptionButton _gameTypeSelector;
    private OptionButton _difficultySelector;
    private CanvasLayer _uiLayer;
    private SetupEditor _setupEditor;
    private RichTextLabel _moveHistoryLabel;
    private ScrollContainer _moveHistoryScroll;
    private HBoxContainer _difficultyContainer;
    private Control _gamePanel;  // World-space panel
    private Button _flipButton;
    private CheckBox _autoFlipCheckbox;
    private Control _promotionPanel;
    private Vector2I _promotionSquare;

    // Multiplayer UI
    private NetworkManager _networkManager;
    private Control _multiplayerPanel;
    private Button _hostButton;
    private Button _joinButton;
    private Button _disconnectButton;
    private Label _networkStatusLabel;
    private TextEdit _sessionCodeDisplay;
    private Button _copyCodeButton;
    private TextEdit _sessionCodeInput;
    private Button _applyCodeButton;
    private Label _sessionCodeLabel;
    private Label _pasteCodeLabel;
    private bool _waitingForOpponent = false;
    private Vector2I? _pendingMoveFrom = null;
    private Vector2I? _pendingMoveTo = null;

    // Board size UI
    private SpinBox _boardSizeInput;

    // FEN UI
    private LineEdit _fenInput;
    private Label _fenStatusLabel;

    private Vector2I? _selectedSquare = null;
    private bool _isDragging = false;
    private Vector2 _dragStart;
    private bool _isInSetupMode = false;

    // Move history
    private List<string> _whiteMoves = new List<string>();
    private List<string> _blackMoves = new List<string>();

    // AI
    private ChessAI _ai;
    private GameType _gameType = GameType.PvP;
    private bool _aiIsThinking = false;
    private bool _playerIsWhite = true;

    // Board flip
    private bool _autoFlipEnabled = false;

    private float _zoomLevel = 0.8f;
    private const float MinZoom = 0.3f;
    private const float MaxZoom = 2.0f;
    private const float ZoomStep = 0.1f;

    public override void _Ready()
    {
        // Create and setup board
        _board = new Board();
        _board.Name = "Board";
        AddChild(_board);

        // Create game manager
        _gameManager = new GameManager();
        _gameManager.Name = "GameManager";
        AddChild(_gameManager);
        _gameManager.Initialize(_board);

        // Create AI
        _ai = new ChessAI(AIDifficulty.Medium);

        // Create network manager
        _networkManager = new NetworkManager();
        _networkManager.Name = "NetworkManager";
        AddChild(_networkManager);

        // Connect network signals
        _networkManager.ConnectionSucceeded += OnNetworkConnectionSucceeded;
        _networkManager.ConnectionFailed += OnNetworkConnectionFailed;
        _networkManager.PeerConnected += OnNetworkPeerConnected;
        _networkManager.PeerDisconnected += OnNetworkPeerDisconnected;
        _networkManager.GameStartReceived += OnNetworkGameStartReceived;
        _networkManager.MoveRequestReceived += OnNetworkMoveRequestReceived;
        _networkManager.MoveConfirmed += OnNetworkMoveConfirmed;
        _networkManager.GameEnded += OnNetworkGameEnded;
        _networkManager.SessionCodeReady += OnSessionCodeReady;
        _networkManager.ConnectionPhaseChanged += OnConnectionPhaseChanged;

        // Create camera for panning and zooming
        _camera = new Camera2D();
        _camera.Name = "Camera";
        AddChild(_camera);
        _camera.MakeCurrent();

        // Center camera on board
        float boardCenter = _board.GetBoardPixelSize() / 2;
        _camera.Position = new Vector2(boardCenter, boardCenter);
        _camera.Zoom = new Vector2(_zoomLevel, _zoomLevel);

        // Create UI
        CreateUI();

        // Create setup editor
        CreateSetupEditor();

        // Connect signals
        _gameManager.TurnChanged += OnTurnChanged;
        _gameManager.GameOver += OnGameOver;
        _gameManager.Check += OnCheck;
        _gameManager.MoveExecuted += OnMoveExecuted;
        _gameManager.PawnPromotionRequested += OnPawnPromotionRequested;

        // Setup initial game
        _gameManager.SetupGame(SetupMode.TwoLines);
        UpdateStatusLabel();
        UpdateModeDescription();
        ClearMoveHistory();
    }

    private void CreateUI()
    {
        // Create CanvasLayer for fixed screen UI (flip button, instructions)
        _uiLayer = new CanvasLayer();
        _uiLayer.Name = "UILayer";
        AddChild(_uiLayer);

        // Create world-space game panel (next to the board)
        CreateWorldSpaceUI();

        // Create fixed screen UI (flip controls, instructions)
        CreateFixedUI();
    }

    private void CreateWorldSpaceUI()
    {
        // World-space panel positioned to the right of the board
        _gamePanel = new Control();
        _gamePanel.Name = "GamePanel";
        float boardSize = _board.GetBoardPixelSize();
        _gamePanel.Position = new Vector2(boardSize + 20, 10);
        AddChild(_gamePanel);

        var mainPanel = new PanelContainer();
        mainPanel.CustomMinimumSize = new Vector2(280, 0);

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color("#1a1a2e", 0.9f);
        panelStyle.SetCornerRadiusAll(8);
        panelStyle.SetContentMarginAll(10);
        mainPanel.AddThemeStyleboxOverride("panel", panelStyle);
        _gamePanel.AddChild(mainPanel);

        var mainVBox = new VBoxContainer();
        mainVBox.AddThemeConstantOverride("separation", 6);
        mainPanel.AddChild(mainVBox);

        // Title
        var titleLabel = new Label();
        titleLabel.Text = "Grand Chess 26";
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        titleLabel.AddThemeColorOverride("font_color", new Color("#ffffff"));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        mainVBox.AddChild(titleLabel);

        // Status label
        _statusLabel = new Label();
        _statusLabel.Name = "StatusLabel";
        _statusLabel.AddThemeColorOverride("font_color", new Color("#aaffaa"));
        _statusLabel.AddThemeFontSizeOverride("font_size", 14);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        mainVBox.AddChild(_statusLabel);

        mainVBox.AddChild(CreateSeparator());

        // Game type selector (PvP / PvE)
        var gameTypeHBox = new HBoxContainer();
        var gameTypeLabel = new Label();
        gameTypeLabel.Text = "Players:";
        gameTypeLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        gameTypeLabel.AddThemeFontSizeOverride("font_size", 12);
        gameTypeLabel.CustomMinimumSize = new Vector2(50, 0);
        gameTypeHBox.AddChild(gameTypeLabel);

        _gameTypeSelector = new OptionButton();
        _gameTypeSelector.CustomMinimumSize = new Vector2(140, 0);
        _gameTypeSelector.AddItem("Player vs Player", (int)GameType.PvP);
        _gameTypeSelector.AddItem("Player vs AI", (int)GameType.PvE);
        _gameTypeSelector.AddItem("Online Multiplayer", (int)GameType.Multiplayer);
        _gameTypeSelector.Selected = 0;
        _gameTypeSelector.ItemSelected += OnGameTypeSelected;
        gameTypeHBox.AddChild(_gameTypeSelector);
        mainVBox.AddChild(gameTypeHBox);

        // Multiplayer panel (hidden by default)
        CreateMultiplayerPanel(mainVBox);

        // AI Difficulty selector (hidden by default)
        _difficultyContainer = new HBoxContainer();
        _difficultyContainer.Visible = false;

        var diffLabel = new Label();
        diffLabel.Text = "AI Level:";
        diffLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        diffLabel.AddThemeFontSizeOverride("font_size", 12);
        diffLabel.CustomMinimumSize = new Vector2(50, 0);
        _difficultyContainer.AddChild(diffLabel);

        _difficultySelector = new OptionButton();
        _difficultySelector.CustomMinimumSize = new Vector2(140, 0);
        foreach (AIDifficulty diff in Enum.GetValues(typeof(AIDifficulty)))
        {
            _difficultySelector.AddItem(ChessAI.GetDifficultyName(diff), (int)diff);
        }
        _difficultySelector.Selected = 1; // Medium
        _difficultySelector.ItemSelected += OnDifficultySelected;
        _difficultyContainer.AddChild(_difficultySelector);
        mainVBox.AddChild(_difficultyContainer);

        // Auto-flip checkbox (only for PvP)
        var autoFlipHBox = new HBoxContainer();
        _autoFlipCheckbox = new CheckBox();
        _autoFlipCheckbox.ButtonPressed = true; // Checked by default
        _autoFlipCheckbox.Text = "Auto-flip board after each move";
        _autoFlipCheckbox.AddThemeColorOverride("font_color", new Color("#cccccc"));
        _autoFlipCheckbox.AddThemeFontSizeOverride("font_size", 12);
        _autoFlipCheckbox.Toggled += OnAutoFlipToggled;
        autoFlipHBox.AddChild(_autoFlipCheckbox);
        mainVBox.AddChild(autoFlipHBox);

        mainVBox.AddChild(CreateSeparator());

        // Game mode selector
        var modeHBox = new HBoxContainer();
        var modeLabel = new Label();
        modeLabel.Text = "Setup:";
        modeLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        modeLabel.AddThemeFontSizeOverride("font_size", 12);
        modeLabel.CustomMinimumSize = new Vector2(50, 0);
        modeHBox.AddChild(modeLabel);

        _modeSelector = new OptionButton();
        _modeSelector.Name = "ModeSelector";
        _modeSelector.CustomMinimumSize = new Vector2(140, 0);

        foreach (SetupMode mode in Enum.GetValues(typeof(SetupMode)))
        {
            _modeSelector.AddItem(SetupManager.GetModeName(mode), (int)mode);
        }

        _modeSelector.Selected = 0;
        _modeSelector.ItemSelected += OnModeSelected;
        modeHBox.AddChild(_modeSelector);
        mainVBox.AddChild(modeHBox);

        // Mode description
        _modeDescLabel = new Label();
        _modeDescLabel.Name = "ModeDescription";
        _modeDescLabel.AddThemeColorOverride("font_color", new Color("#888888"));
        _modeDescLabel.AddThemeFontSizeOverride("font_size", 10);
        _modeDescLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _modeDescLabel.CustomMinimumSize = new Vector2(260, 35);
        mainVBox.AddChild(_modeDescLabel);

        // Board size input
        var boardSizeHBox = new HBoxContainer();
        var boardSizeLabel = new Label();
        boardSizeLabel.Text = "Size:";
        boardSizeLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        boardSizeLabel.AddThemeFontSizeOverride("font_size", 12);
        boardSizeLabel.CustomMinimumSize = new Vector2(50, 0);
        boardSizeHBox.AddChild(boardSizeLabel);

        _boardSizeInput = new SpinBox();
        _boardSizeInput.MinValue = 1;
        _boardSizeInput.MaxValue = 99;
        _boardSizeInput.Value = Board.BoardSize;
        _boardSizeInput.Step = 1;
        _boardSizeInput.CustomMinimumSize = new Vector2(80, 0);
        _boardSizeInput.TooltipText = $"Board size {Board.MinBoardSize}-{Board.MaxBoardSize} (applies on New Game)";
        boardSizeHBox.AddChild(_boardSizeInput);

        var sizeInfoLabel = new Label();
        sizeInfoLabel.Text = "x" + Board.BoardSize.ToString();
        sizeInfoLabel.AddThemeColorOverride("font_color", new Color("#888888"));
        sizeInfoLabel.AddThemeFontSizeOverride("font_size", 11);
        sizeInfoLabel.Name = "SizeInfoLabel";
        boardSizeHBox.AddChild(sizeInfoLabel);
        _boardSizeInput.ValueChanged += (value) => {
            sizeInfoLabel.Text = "x" + ((int)value).ToString();
        };
        mainVBox.AddChild(boardSizeHBox);

        // Piece style selector
        var pieceStyleHBox = new HBoxContainer();
        var pieceStyleLabel = new Label();
        pieceStyleLabel.Text = "Pieces:";
        pieceStyleLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        pieceStyleLabel.AddThemeFontSizeOverride("font_size", 12);
        pieceStyleLabel.CustomMinimumSize = new Vector2(50, 0);
        pieceStyleHBox.AddChild(pieceStyleLabel);

        var pieceStyleSelector = new OptionButton();
        pieceStyleSelector.CustomMinimumSize = new Vector2(140, 0);
        for (int i = 0; i < Board.PieceStyles.Length; i++)
        {
            pieceStyleSelector.AddItem(Board.PieceStyleNames[i], i);
        }
        pieceStyleSelector.Selected = 0;
        pieceStyleSelector.ItemSelected += (index) =>
        {
            _board.SetPieceStyle(Board.PieceStyles[index]);
        };
        pieceStyleHBox.AddChild(pieceStyleSelector);
        mainVBox.AddChild(pieceStyleHBox);

        // New Game button
        var resetButton = new Button();
        resetButton.Name = "ResetButton";
        resetButton.Text = "New Game";
        resetButton.CustomMinimumSize = new Vector2(0, 30);
        resetButton.Pressed += OnResetButtonPressed;
        mainVBox.AddChild(resetButton);

        mainVBox.AddChild(CreateSeparator());

        // FEN Import/Export section
        var fenLabel = new Label();
        fenLabel.Text = "FEN Code";
        fenLabel.AddThemeFontSizeOverride("font_size", 12);
        fenLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        mainVBox.AddChild(fenLabel);

        _fenInput = new LineEdit();
        _fenInput.PlaceholderText = "Paste FEN code here...";
        _fenInput.CustomMinimumSize = new Vector2(260, 0);
        mainVBox.AddChild(_fenInput);

        var fenButtonHBox = new HBoxContainer();
        fenButtonHBox.AddThemeConstantOverride("separation", 4);

        var importButton = new Button();
        importButton.Text = "Import";
        importButton.CustomMinimumSize = new Vector2(80, 28);
        importButton.Pressed += OnFENImportPressed;
        fenButtonHBox.AddChild(importButton);

        var exportButton = new Button();
        exportButton.Text = "Export";
        exportButton.CustomMinimumSize = new Vector2(80, 28);
        exportButton.Pressed += OnFENExportPressed;
        fenButtonHBox.AddChild(exportButton);

        var copyButton = new Button();
        copyButton.Text = "Copy";
        copyButton.CustomMinimumSize = new Vector2(60, 28);
        copyButton.Pressed += OnFENCopyPressed;
        fenButtonHBox.AddChild(copyButton);

        mainVBox.AddChild(fenButtonHBox);

        _fenStatusLabel = new Label();
        _fenStatusLabel.Text = "";
        _fenStatusLabel.AddThemeFontSizeOverride("font_size", 10);
        _fenStatusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _fenStatusLabel.CustomMinimumSize = new Vector2(260, 0);
        mainVBox.AddChild(_fenStatusLabel);

        mainVBox.AddChild(CreateSeparator());

        // Move History section
        var historyLabel = new Label();
        historyLabel.Text = "Move History";
        historyLabel.AddThemeFontSizeOverride("font_size", 12);
        historyLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        mainVBox.AddChild(historyLabel);

        // Scrollable move history
        _moveHistoryScroll = new ScrollContainer();
        _moveHistoryScroll.CustomMinimumSize = new Vector2(260, 180);
        _moveHistoryScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var scrollStyle = new StyleBoxFlat();
        scrollStyle.BgColor = new Color("#0a0a15", 0.8f);
        scrollStyle.SetCornerRadiusAll(4);
        scrollStyle.SetContentMarginAll(5);

        var scrollPanel = new PanelContainer();
        scrollPanel.AddThemeStyleboxOverride("panel", scrollStyle);
        scrollPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _moveHistoryLabel = new RichTextLabel();
        _moveHistoryLabel.Name = "MoveHistory";
        _moveHistoryLabel.BbcodeEnabled = true;
        _moveHistoryLabel.FitContent = true;
        _moveHistoryLabel.ScrollFollowing = true;
        _moveHistoryLabel.CustomMinimumSize = new Vector2(250, 0);
        _moveHistoryLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _moveHistoryLabel.AddThemeColorOverride("default_color", new Color("#dddddd"));

        scrollPanel.AddChild(_moveHistoryLabel);
        _moveHistoryScroll.AddChild(scrollPanel);
        mainVBox.AddChild(_moveHistoryScroll);

        mainVBox.AddChild(CreateSeparator());

        // Keyboard shortcuts
        var shortcutsLabel = new Label();
        shortcutsLabel.Text = "Scroll: Zoom | Middle-drag: Pan\nR: Reset | F: Flip | 1-4: Modes";
        shortcutsLabel.AddThemeColorOverride("font_color", new Color("#666666"));
        shortcutsLabel.AddThemeFontSizeOverride("font_size", 10);
        mainVBox.AddChild(shortcutsLabel);
    }

    private void CreateFixedUI()
    {
        // Fixed screen UI panel (top-left) for flip controls
        var flipPanel = new PanelContainer();
        flipPanel.Position = new Vector2(10, 10);

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color("#1a1a2e", 0.9f);
        panelStyle.SetCornerRadiusAll(8);
        panelStyle.SetContentMarginAll(8);
        flipPanel.AddThemeStyleboxOverride("panel", panelStyle);
        _uiLayer.AddChild(flipPanel);

        var flipVBox = new VBoxContainer();
        flipVBox.AddThemeConstantOverride("separation", 4);
        flipPanel.AddChild(flipVBox);

        // Flip button
        _flipButton = new Button();
        _flipButton.Text = "Flip Board";
        _flipButton.CustomMinimumSize = new Vector2(100, 28);
        _flipButton.Pressed += OnFlipButtonPressed;
        flipVBox.AddChild(_flipButton);

        // Create promotion UI (hidden by default)
        CreatePromotionUI();
    }

    private void CreatePromotionUI()
    {
        _promotionPanel = new Control();
        _promotionPanel.Name = "PromotionPanel";
        _promotionPanel.Visible = false;
        _uiLayer.AddChild(_promotionPanel);

        var panelContainer = new PanelContainer();
        panelContainer.Name = "PanelContainer";
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color("#1a1a2e", 0.95f);
        panelStyle.SetCornerRadiusAll(8);
        panelStyle.SetContentMarginAll(10);
        panelStyle.BorderColor = new Color("#4a4a6e");
        panelStyle.SetBorderWidthAll(2);
        panelContainer.AddThemeStyleboxOverride("panel", panelStyle);
        _promotionPanel.AddChild(panelContainer);

        var vbox = new VBoxContainer();
        vbox.Name = "VBoxContainer";
        vbox.AddThemeConstantOverride("separation", 8);
        panelContainer.AddChild(vbox);

        var titleLabel = new Label();
        titleLabel.Text = "Promote Pawn To:";
        titleLabel.AddThemeFontSizeOverride("font_size", 14);
        titleLabel.AddThemeColorOverride("font_color", new Color("#ffffff"));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(titleLabel);

        var buttonBox = new HBoxContainer();
        buttonBox.Name = "HBoxContainer";
        buttonBox.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(buttonBox);

        // Create promotion buttons with piece symbols
        var pieces = new (PieceType type, string whiteSymbol, string blackSymbol)[]
        {
            (PieceType.Queen, "\u2655", "\u265B"),
            (PieceType.Rook, "\u2656", "\u265C"),
            (PieceType.Bishop, "\u2657", "\u265D"),
            (PieceType.Knight, "\u2658", "\u265E")
        };

        foreach (var (pieceType, whiteSymbol, blackSymbol) in pieces)
        {
            var button = new Button();
            button.Name = $"Promote{pieceType}";
            button.CustomMinimumSize = new Vector2(50, 50);
            button.AddThemeFontSizeOverride("font_size", 28);
            // Symbol will be updated when showing the panel
            button.Text = whiteSymbol;
            button.Pressed += () => OnPromotionSelected(pieceType);
            buttonBox.AddChild(button);
        }
    }

    private HSeparator CreateSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 4);
        return sep;
    }

    private void CreateMultiplayerPanel(VBoxContainer parent)
    {
        _multiplayerPanel = new VBoxContainer();
        _multiplayerPanel.Name = "MultiplayerPanel";
        _multiplayerPanel.Visible = false;
        ((VBoxContainer)_multiplayerPanel).AddThemeConstantOverride("separation", 4);
        parent.AddChild(_multiplayerPanel);

        // Network status
        _networkStatusLabel = new Label();
        _networkStatusLabel.Text = "Not connected";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#888888"));
        _networkStatusLabel.AddThemeFontSizeOverride("font_size", 11);
        _networkStatusLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _networkStatusLabel.CustomMinimumSize = new Vector2(260, 0);
        _multiplayerPanel.AddChild(_networkStatusLabel);

        // Host / Join / Leave buttons
        var buttonHBox = new HBoxContainer();
        buttonHBox.AddThemeConstantOverride("separation", 4);

        _hostButton = new Button();
        _hostButton.Text = "Create Game";
        _hostButton.CustomMinimumSize = new Vector2(90, 28);
        _hostButton.Pressed += OnHostButtonPressed;
        buttonHBox.AddChild(_hostButton);

        _joinButton = new Button();
        _joinButton.Text = "Join Game";
        _joinButton.CustomMinimumSize = new Vector2(90, 28);
        _joinButton.Pressed += OnJoinButtonPressed;
        buttonHBox.AddChild(_joinButton);

        _disconnectButton = new Button();
        _disconnectButton.Text = "Leave";
        _disconnectButton.CustomMinimumSize = new Vector2(60, 28);
        _disconnectButton.Visible = false;
        _disconnectButton.Pressed += OnDisconnectButtonPressed;
        buttonHBox.AddChild(_disconnectButton);

        _multiplayerPanel.AddChild(buttonHBox);

        // Session code display (read-only, shows generated code)
        _sessionCodeLabel = new Label();
        _sessionCodeLabel.Text = "Your session code:";
        _sessionCodeLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        _sessionCodeLabel.AddThemeFontSizeOverride("font_size", 11);
        _sessionCodeLabel.Visible = false;
        _multiplayerPanel.AddChild(_sessionCodeLabel);

        _sessionCodeDisplay = new TextEdit();
        _sessionCodeDisplay.CustomMinimumSize = new Vector2(260, 60);
        _sessionCodeDisplay.Editable = false;
        _sessionCodeDisplay.WrapMode = TextEdit.LineWrappingMode.Boundary;
        _sessionCodeDisplay.AddThemeFontSizeOverride("font_size", 10);
        _sessionCodeDisplay.Visible = false;
        _multiplayerPanel.AddChild(_sessionCodeDisplay);

        _copyCodeButton = new Button();
        _copyCodeButton.Text = "Copy Code";
        _copyCodeButton.CustomMinimumSize = new Vector2(90, 28);
        _copyCodeButton.Pressed += OnCopyCodePressed;
        _copyCodeButton.Visible = false;
        _multiplayerPanel.AddChild(_copyCodeButton);

        // Paste area for other player's code
        _pasteCodeLabel = new Label();
        _pasteCodeLabel.Text = "Paste other player's code:";
        _pasteCodeLabel.AddThemeColorOverride("font_color", new Color("#cccccc"));
        _pasteCodeLabel.AddThemeFontSizeOverride("font_size", 11);
        _pasteCodeLabel.Visible = false;
        _multiplayerPanel.AddChild(_pasteCodeLabel);

        _sessionCodeInput = new TextEdit();
        _sessionCodeInput.CustomMinimumSize = new Vector2(260, 60);
        _sessionCodeInput.PlaceholderText = "Paste session code here...";
        _sessionCodeInput.WrapMode = TextEdit.LineWrappingMode.Boundary;
        _sessionCodeInput.AddThemeFontSizeOverride("font_size", 10);
        _sessionCodeInput.Visible = false;
        _multiplayerPanel.AddChild(_sessionCodeInput);

        _applyCodeButton = new Button();
        _applyCodeButton.Text = "Connect";
        _applyCodeButton.CustomMinimumSize = new Vector2(90, 28);
        _applyCodeButton.Pressed += OnApplyCodePressed;
        _applyCodeButton.Visible = false;
        _multiplayerPanel.AddChild(_applyCodeButton);

        // Check WebRTC availability
        if (!_networkManager.IsWebRtcAvailable)
        {
            _networkStatusLabel.Text = "WebRTC extension not found. Online multiplayer unavailable.";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
            _hostButton.Disabled = true;
            _joinButton.Disabled = true;
        }
    }

    private void CreateSetupEditor()
    {
        _setupEditor = new SetupEditor();
        _setupEditor.Name = "SetupEditor";
        _setupEditor.Visible = false;
        _setupEditor.Initialize(_board);

        _setupEditor.SetupComplete += OnSetupComplete;
        _setupEditor.SetupCancelled += OnSetupCancelled;

        _uiLayer.AddChild(_setupEditor);
    }

    public override void _Input(InputEvent @event)
    {
        // Don't process input while AI is thinking or awaiting promotion
        if (_aiIsThinking || _gameManager.IsAwaitingPromotion)
            return;

        if (@event is InputEventMouseButton mouseButton)
        {
            // Only zoom if not currently dragging (prevents zoom while panning with middle mouse)
            if (mouseButton.ButtonIndex == MouseButton.WheelUp && !_isDragging)
            {
                Zoom(ZoomStep);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown && !_isDragging)
            {
                Zoom(-ZoomStep);
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                if (mouseButton.Pressed)
                {
                    if (_isInSetupMode)
                    {
                        // Right-click removes pieces in setup mode
                        Vector2I? square = ScreenToBoard(mouseButton.Position);
                        if (square.HasValue)
                        {
                            _setupEditor.HandleBoardClick(square.Value, true);
                        }
                    }
                }
                else
                {
                    _isDragging = false;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                // Middle mouse button pans camera in all modes
                if (mouseButton.Pressed)
                {
                    _isDragging = true;
                    _dragStart = mouseButton.Position;
                }
                else
                {
                    _isDragging = false;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
            {
                HandleLeftClick(mouseButton.Position);
            }
        }

        if (@event is InputEventMouseMotion mouseMotion && _isDragging)
        {
            Vector2 delta = _dragStart - mouseMotion.Position;
            _camera.Position += delta / _camera.Zoom.X;
            _dragStart = mouseMotion.Position;
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            // Don't process keyboard shortcuts when typing in a text field
            var focusedControl = GetViewport().GuiGetFocusOwner();
            if (focusedControl is LineEdit || focusedControl is TextEdit)
            {
                return;
            }

            if (keyEvent.Keycode == Key.R && !_isInSetupMode && !(_gameType == GameType.Multiplayer && _networkManager.IsOnline && !_networkManager.IsHost))
            {
                OnResetButtonPressed();
            }
            else if (keyEvent.Keycode == Key.F && !_isInSetupMode && !_autoFlipEnabled)
            {
                _board.FlipBoard();
            }
            else if (keyEvent.Keycode == Key.Escape)
            {
                if (_isInSetupMode)
                {
                    OnSetupCancelled();
                }
                else
                {
                    DeselectPiece();
                }
            }
            else if (keyEvent.Keycode >= Key.Key1 && keyEvent.Keycode <= Key.Key4)
            {
                // In multiplayer, only host can change modes
                if (_gameType == GameType.Multiplayer && _networkManager.IsOnline && !_networkManager.IsHost)
                {
                    return;
                }

                if (keyEvent.Keycode == Key.Key1)
                {
                    SelectMode(SetupMode.TwoLines);
                }
                else if (keyEvent.Keycode == Key.Key2)
                {
                    SelectMode(SetupMode.OneLine);
                }
                else if (keyEvent.Keycode == Key.Key3)
                {
                    SelectMode(SetupMode.ThreeLines);
                }
                else if (keyEvent.Keycode == Key.Key4)
                {
                    SelectMode(SetupMode.Custom);
                }
            }
        }
    }

    private Vector2I? ScreenToBoard(Vector2 screenPos)
    {
        Vector2 screenCenter = GetViewport().GetVisibleRect().Size / 2;
        Vector2 offsetFromCenter = screenPos - screenCenter;
        Vector2 worldPos = _camera.Position + offsetFromCenter / _camera.Zoom.X;
        return _board.PixelToSquare(worldPos);
    }

    private void Zoom(float delta)
    {
        _zoomLevel = Mathf.Clamp(_zoomLevel + delta, MinZoom, MaxZoom);
        _camera.Zoom = new Vector2(_zoomLevel, _zoomLevel);
    }

    private void HandleLeftClick(Vector2 screenPos)
    {
        Vector2I? clickedSquare = ScreenToBoard(screenPos);

        if (!clickedSquare.HasValue)
        {
            if (!_isInSetupMode)
            {
                DeselectPiece();
            }
            return;
        }

        if (_isInSetupMode)
        {
            _setupEditor.HandleBoardClick(clickedSquare.Value, false);
            return;
        }

        if (_gameManager.CurrentState != GameState.Playing)
        {
            return;
        }

        // In PvE mode, only allow player to move their pieces
        if (_gameType == GameType.PvE && _gameManager.IsWhiteTurn != _playerIsWhite)
        {
            return;
        }

        // In Multiplayer mode, check if it's my turn
        if (_gameType == GameType.Multiplayer && !IsMyTurnInMultiplayer())
        {
            return;
        }

        if (_selectedSquare.HasValue)
        {
            // Check if this is a valid move
            var legalMoves = _gameManager.GetLegalMoves(_selectedSquare.Value);
            if (legalMoves.Contains(clickedSquare.Value))
            {
                if (_gameType == GameType.Multiplayer && _networkManager.IsOnline)
                {
                    // In multiplayer, send move request
                    HandleMultiplayerMove(_selectedSquare.Value, clickedSquare.Value);
                }
                else
                {
                    _gameManager.TryMakeMove(_selectedSquare.Value, clickedSquare.Value);
                }
                DeselectPiece();
            }
            else
            {
                Piece clickedPiece = _board.GetPiece(clickedSquare.Value);
                if (clickedPiece != null && clickedPiece.IsWhite == _gameManager.IsWhiteTurn)
                {
                    SelectPiece(clickedSquare.Value);
                }
                else
                {
                    DeselectPiece();
                }
            }
        }
        else
        {
            Piece clickedPiece = _board.GetPiece(clickedSquare.Value);
            if (clickedPiece != null && clickedPiece.IsWhite == _gameManager.IsWhiteTurn)
            {
                SelectPiece(clickedSquare.Value);
            }
        }
    }

    private void HandleMultiplayerMove(Vector2I from, Vector2I to)
    {
        Piece piece = _board.GetPiece(from);

        // Check if this is a pawn promotion move
        if (piece is Pawn)
        {
            int promotionRank = piece.IsWhite ? Board.BoardSize - 1 : 0;
            if (to.Y == promotionRank)
            {
                // Store the pending move for later (after promotion choice)
                _pendingMoveFrom = from;
                _pendingMoveTo = to;
                _promotionSquare = to;

                if (_networkManager.IsHost)
                {
                    // Host executes the move which will trigger promotion UI
                    _gameManager.TryMakeMove(from, to);
                }
                else
                {
                    // Client needs to choose promotion first, then send
                    // Show promotion UI directly
                    ShowPromotionUI(to, piece.IsWhite);
                }
                return;
            }
        }

        if (_networkManager.IsHost)
        {
            // Host executes move directly and broadcasts
            if (_gameManager.TryMakeMove(from, to))
            {
                _networkManager.ConfirmMove(from, to, null);

                if (_gameManager.CurrentState != GameState.Playing)
                {
                    _networkManager.SendGameEnd(_gameManager.CurrentState);
                }
            }
        }
        else
        {
            // Client sends request to host
            _networkManager.RequestMove(from, to, null);
        }
    }

    private void ShowPromotionUI(Vector2I square, bool isWhite)
    {
        // Update button symbols based on piece color
        var buttonBox = _promotionPanel.GetNode<PanelContainer>("PanelContainer")
            .GetNode<VBoxContainer>("VBoxContainer")
            .GetNode<HBoxContainer>("HBoxContainer");

        var pieces = new (string name, string whiteSymbol, string blackSymbol)[]
        {
            ("PromoteQueen", "\u2655", "\u265B"),
            ("PromoteRook", "\u2656", "\u265C"),
            ("PromoteBishop", "\u2657", "\u265D"),
            ("PromoteKnight", "\u2658", "\u265E")
        };

        foreach (var (name, whiteSymbol, blackSymbol) in pieces)
        {
            var button = buttonBox.GetNode<Button>(name);
            button.Text = isWhite ? whiteSymbol : blackSymbol;
        }

        // Position the panel in the center of the screen
        var viewportSize = GetViewport().GetVisibleRect().Size;
        _promotionPanel.Position = new Vector2(
            (viewportSize.X - 230) / 2,
            (viewportSize.Y - 100) / 2
        );

        _promotionPanel.Visible = true;
        _statusLabel.Text = "Choose promotion piece";
        _statusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
    }

    private void SelectPiece(Vector2I square)
    {
        _selectedSquare = square;
        _board.SetSelectedSquare(square);

        var legalMoves = _gameManager.GetLegalMoves(square);
        _board.SetLegalMoves(legalMoves);
    }

    private void DeselectPiece()
    {
        _selectedSquare = null;
        _board.ClearHighlights();
    }

    private void OnTurnChanged(bool isWhiteTurn)
    {
        UpdateStatusLabel();

        // Trigger AI move if it's AI's turn
        if (_gameType == GameType.PvE && _gameManager.CurrentState == GameState.Playing)
        {
            bool isAITurn = isWhiteTurn != _playerIsWhite;
            if (isAITurn)
            {
                TriggerAIMove();
            }
        }
    }

    private async void TriggerAIMove()
    {
        if (_aiIsThinking || _gameManager.CurrentState != GameState.Playing)
            return;

        _aiIsThinking = true;
        UpdateStatusLabel();

        // Small delay so UI updates
        await ToSignal(GetTree().CreateTimer(0.1f), "timeout");

        bool aiIsWhite = !_playerIsWhite;
        var move = await _ai.GetBestMoveAsync(_board, _gameManager, aiIsWhite);

        _aiIsThinking = false;

        if (move.HasValue && _gameManager.CurrentState == GameState.Playing)
        {
            _gameManager.TryMakeMove(move.Value.from, move.Value.to);
        }

        UpdateStatusLabel();
    }

    private void OnGameOver(long state)
    {
        GameState gameState = (GameState)state;
        string message = gameState switch
        {
            GameState.WhiteWins => "CHECKMATE! White wins!",
            GameState.BlackWins => "CHECKMATE! Black wins!",
            GameState.Stalemate => "STALEMATE! Draw!",
            GameState.Draw => "DRAW! (100-move rule)",
            _ => ""
        };

        _statusLabel.Text = message;
        _statusLabel.AddThemeColorOverride("font_color", new Color("#ffaa00"));
    }

    private void OnCheck(bool isWhiteInCheck)
    {
        string player = isWhiteInCheck ? "White" : "Black";
        _statusLabel.Text = $"{player} is in CHECK!";
        _statusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
    }

    private void OnPawnPromotionRequested(Vector2I square, bool isWhite)
    {
        _promotionSquare = square;

        // In PvE mode, auto-promote AI pawns to Queen
        if (_gameType == GameType.PvE && isWhite != _playerIsWhite)
        {
            _gameManager.CompletePromotion(PieceType.Queen);
            return;
        }

        // In Multiplayer, client handles promotion separately via ShowPromotionUI
        // This signal handler is only for host promotions in multiplayer
        if (_gameType == GameType.Multiplayer && _networkManager.IsOnline && !_networkManager.IsHost)
        {
            // Client doesn't use this signal - promotion UI is shown directly
            return;
        }

        ShowPromotionUI(square, isWhite);
    }

    private void OnPromotionSelected(PieceType pieceType)
    {
        _promotionPanel.Visible = false;

        // Handle multiplayer promotion
        if (_gameType == GameType.Multiplayer && _networkManager.IsOnline && _pendingMoveFrom.HasValue && _pendingMoveTo.HasValue)
        {
            if (_networkManager.IsHost)
            {
                _gameManager.CompletePromotion(pieceType);
                _networkManager.ConfirmMove(_pendingMoveFrom.Value, _pendingMoveTo.Value, pieceType);

                if (_gameManager.CurrentState != GameState.Playing)
                {
                    _networkManager.SendGameEnd(_gameManager.CurrentState);
                }
            }
            else
            {
                // Client sends the move with promotion to host
                _networkManager.RequestMove(_pendingMoveFrom.Value, _pendingMoveTo.Value, pieceType);
            }
            _pendingMoveFrom = null;
            _pendingMoveTo = null;
            DeselectPiece();
        }
        else
        {
            _gameManager.CompletePromotion(pieceType);
        }
    }

    private void OnMoveExecuted(string notation)
    {
        // White always moves first, so use move counts to determine whose move this is
        // If counts are equal, it's white's turn to add a move
        // If white has more moves, it's black's turn to add a move
        if (_whiteMoves.Count == _blackMoves.Count)
        {
            _whiteMoves.Add(notation);
        }
        else
        {
            _blackMoves.Add(notation);
        }

        UpdateMoveHistory();

        // Auto-flip board after move (only in PvP mode)
        if (_autoFlipEnabled && _gameType == GameType.PvP)
        {
            _board.FlipBoard();
        }
    }

    private void OnFlipButtonPressed()
    {
        _board.FlipBoard();
    }

    private void OnFENImportPressed()
    {
        string fen = _fenInput.Text.Trim();
        if (string.IsNullOrEmpty(fen))
        {
            _fenStatusLabel.Text = "Please enter a FEN code";
            _fenStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
            return;
        }

        var (success, error) = FENManager.ImportFEN(fen, _board, _gameManager);

        if (success)
        {
            // Update board size input to match imported FEN
            _boardSizeInput.Value = Board.BoardSize;

            // Recenter camera on new board
            float boardCenter = _board.GetBoardPixelSize() / 2;
            _camera.Position = new Vector2(boardCenter, boardCenter);

            // Update game panel position
            float boardSize = _board.GetBoardPixelSize();
            _gamePanel.Position = new Vector2(boardSize + 20, 10);

            _fenStatusLabel.Text = "FEN imported successfully!";
            _fenStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));

            DeselectPiece();
            UpdateStatusLabel();
            ClearMoveHistory();
        }
        else
        {
            _fenStatusLabel.Text = error;
            _fenStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
        }
    }

    private void OnFENExportPressed()
    {
        string fen = FENManager.ExportFEN(_board, _gameManager);
        _fenInput.Text = fen;
        _fenStatusLabel.Text = "FEN exported to text field";
        _fenStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
    }

    private void OnFENCopyPressed()
    {
        string fen = _fenInput.Text;
        if (string.IsNullOrEmpty(fen))
        {
            fen = FENManager.ExportFEN(_board, _gameManager);
            _fenInput.Text = fen;
        }
        DisplayServer.ClipboardSet(fen);
        _fenStatusLabel.Text = "FEN copied to clipboard!";
        _fenStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
    }

    private void OnAutoFlipToggled(bool toggled)
    {
        _autoFlipEnabled = toggled;
        // Hide/show flip button based on auto-flip state
        _flipButton.Visible = !_autoFlipEnabled;
    }

    private void UpdateMoveHistory()
    {
        string historyText = "";

        int moveCount = Math.Max(_whiteMoves.Count, _blackMoves.Count);

        for (int i = 0; i < moveCount; i++)
        {
            string whiteMove = i < _whiteMoves.Count ? _whiteMoves[i] : "";
            string blackMove = i < _blackMoves.Count ? _blackMoves[i] : "";

            historyText += $"[color=#888888]{i + 1}.[/color] ";
            historyText += $"[color=#ffffff]{whiteMove}[/color]";

            if (!string.IsNullOrEmpty(blackMove))
            {
                historyText += $"  [color=#aaaaaa]{blackMove}[/color]";
            }

            historyText += "\n";
        }

        _moveHistoryLabel.Text = historyText;
        CallDeferred(nameof(ScrollToBottom));
    }

    private void ScrollToBottom()
    {
        _moveHistoryScroll.ScrollVertical = (int)_moveHistoryScroll.GetVScrollBar().MaxValue;
    }

    private void ClearMoveHistory()
    {
        _whiteMoves.Clear();
        _blackMoves.Clear();
        _moveHistoryLabel.Text = "[color=#666666]No moves yet[/color]";
    }

    private void UpdateStatusLabel()
    {
        if (_isInSetupMode)
        {
            _statusLabel.Text = "Place pieces on the board";
            _statusLabel.AddThemeColorOverride("font_color", new Color("#aaaaff"));
        }
        else if (_gameManager.IsAwaitingPromotion)
        {
            _statusLabel.Text = "Choose promotion piece";
            _statusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
        }
        else if (_aiIsThinking)
        {
            _statusLabel.Text = "AI is thinking...";
            _statusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
        }
        else if (_gameType == GameType.Multiplayer)
        {
            if (!_networkManager.IsOnline)
            {
                _statusLabel.Text = "Host or join a game";
                _statusLabel.AddThemeColorOverride("font_color", new Color("#888888"));
            }
            else if (_waitingForOpponent)
            {
                _statusLabel.Text = "Waiting for opponent...";
                _statusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
            }
            else if (_gameManager.CurrentState == GameState.Playing)
            {
                string turn = _gameManager.IsWhiteTurn ? "White" : "Black";
                bool isMyTurn = IsMyTurnInMultiplayer();

                if (isMyTurn)
                {
                    _statusLabel.Text = $"Your turn ({turn})";
                    _statusLabel.AddThemeColorOverride("font_color", new Color("#aaffaa"));
                }
                else
                {
                    _statusLabel.Text = $"Opponent's turn ({turn})";
                    _statusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
                }
            }
        }
        else if (_gameManager.CurrentState == GameState.Playing)
        {
            string turn = _gameManager.IsWhiteTurn ? "White" : "Black";

            if (_gameType == GameType.PvE)
            {
                bool isPlayerTurn = _gameManager.IsWhiteTurn == _playerIsWhite;
                if (isPlayerTurn)
                {
                    _statusLabel.Text = $"Your turn ({turn})";
                }
                else
                {
                    _statusLabel.Text = $"AI's turn ({turn})";
                }
            }
            else
            {
                _statusLabel.Text = $"{turn} to move";
            }

            _statusLabel.AddThemeColorOverride("font_color", new Color("#aaffaa"));
        }
    }

    private void UpdateModeDescription()
    {
        SetupMode currentMode = _isInSetupMode ? SetupMode.Custom : _gameManager.CurrentSetupMode;
        _modeDescLabel.Text = SetupManager.GetModeDescription(currentMode);
    }

    private void OnGameTypeSelected(long index)
    {
        int typeId = _gameTypeSelector.GetItemId((int)index);
        GameType newType = (GameType)typeId;

        // If switching away from multiplayer, disconnect
        if (_gameType == GameType.Multiplayer && newType != GameType.Multiplayer && _networkManager.IsOnline)
        {
            _networkManager.Disconnect();
            ResetMultiplayerUI();
        }

        _gameType = newType;

        // Show/hide difficulty selector
        _difficultyContainer.Visible = (_gameType == GameType.PvE);

        // Show/hide auto-flip checkbox (only for PvP)
        _autoFlipCheckbox.Visible = (_gameType == GameType.PvP);

        // Show/hide multiplayer panel
        _multiplayerPanel.Visible = (_gameType == GameType.Multiplayer);

        // If switching to PvE, disable auto-flip and show flip button
        if (_gameType == GameType.PvE && _autoFlipEnabled)
        {
            _autoFlipEnabled = false;
            _autoFlipCheckbox.ButtonPressed = false;
            _flipButton.Visible = true;
        }

        // Don't start a new game in multiplayer mode - wait for connection
        if (_gameType != GameType.Multiplayer)
        {
            StartNewGame(_gameManager.CurrentSetupMode);
        }
        else
        {
            UpdateStatusLabel();
        }
    }

    private void OnDifficultySelected(long index)
    {
        int diffId = _difficultySelector.GetItemId((int)index);
        _ai.Difficulty = (AIDifficulty)diffId;
    }

    private void OnModeSelected(long index)
    {
        int modeId = _modeSelector.GetItemId((int)index);
        SetupMode mode = (SetupMode)modeId;

        // In multiplayer, only host can change mode and only before/after games
        if (_gameType == GameType.Multiplayer && _networkManager.IsOnline)
        {
            if (!_networkManager.IsHost)
            {
                // Revert selection for client
                _modeSelector.Selected = (int)_gameManager.CurrentSetupMode;
                return;
            }
            // Host can change mode, will restart game
            _gameManager.CurrentSetupMode = mode;
            if (!_waitingForOpponent)
            {
                StartMultiplayerGame();
            }
            return;
        }

        if (mode == SetupMode.Custom)
        {
            EnterSetupMode();
        }
        else
        {
            ExitSetupMode();
            StartNewGame(mode);
        }
    }

    private void SelectMode(SetupMode mode)
    {
        _modeSelector.Selected = (int)mode;

        // In multiplayer, changing mode restarts the game
        if (_gameType == GameType.Multiplayer && _networkManager.IsOnline && _networkManager.IsHost)
        {
            _gameManager.CurrentSetupMode = mode;
            if (!_waitingForOpponent)
            {
                StartMultiplayerGame();
            }
            return;
        }

        if (mode == SetupMode.Custom)
        {
            EnterSetupMode();
        }
        else
        {
            ExitSetupMode();
            StartNewGame(mode);
        }
    }

    private void EnterSetupMode()
    {
        _isInSetupMode = true;
        _setupEditor.Show();
        DeselectPiece();
        UpdateStatusLabel();
        UpdateModeDescription();
        ClearMoveHistory();
    }

    private void ExitSetupMode()
    {
        _isInSetupMode = false;
        _setupEditor.Hide();
    }

    private void OnSetupComplete()
    {
        ExitSetupMode();
        _gameManager.SetupGame(SetupMode.Custom);
        UpdateStatusLabel();
        UpdateModeDescription();
        ClearMoveHistory();
    }

    private void OnSetupCancelled()
    {
        ExitSetupMode();
        _modeSelector.Selected = (int)_gameManager.CurrentSetupMode;
        _gameManager.ResetGame();
        UpdateStatusLabel();
        UpdateModeDescription();
        ClearMoveHistory();
    }

    private void StartNewGame(SetupMode mode)
    {
        _aiIsThinking = false;
        _promotionPanel.Visible = false;

        // Get board size from input - read directly from LineEdit to get uncommitted text
        int newSize;
        var lineEdit = _boardSizeInput.GetLineEdit();
        if (int.TryParse(lineEdit.Text, out int parsed))
        {
            newSize = Math.Clamp(parsed, Board.MinBoardSize, Board.MaxBoardSize);
            _boardSizeInput.Value = newSize; // Sync the SpinBox value
        }
        else
        {
            newSize = (int)_boardSizeInput.Value;
        }
        if (newSize != Board.BoardSize)
        {
            _board.SetBoardSize(newSize);

            // Recenter camera on new board
            float boardCenter = _board.GetBoardPixelSize() / 2;
            _camera.Position = new Vector2(boardCenter, boardCenter);

            // Update game panel position
            float boardPixelSize = _board.GetBoardPixelSize();
            _gamePanel.Position = new Vector2(boardPixelSize + 20, 10);
        }

        _gameManager.SetupGame(mode);
        DeselectPiece();
        UpdateStatusLabel();
        UpdateModeDescription();
        ClearMoveHistory();
        // Reset board orientation
        _board.SetFlipped(false);
    }

    private void OnResetButtonPressed()
    {
        if (_isInSetupMode || _aiIsThinking)
        {
            return;
        }

        // In multiplayer, only host can reset and it restarts the game
        if (_gameType == GameType.Multiplayer && _networkManager.IsOnline)
        {
            if (_networkManager.IsHost)
            {
                StartMultiplayerGame();
            }
            return;
        }

        // Use StartNewGame to apply board size from input
        StartNewGame(_gameManager.CurrentSetupMode);
    }

    // === Multiplayer Methods ===

    private void OnHostButtonPressed()
    {
        _networkManager.HostGame();
        _networkStatusLabel.Text = "Generating session code...";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
        _hostButton.Visible = false;
        _joinButton.Visible = false;
        _disconnectButton.Visible = true;
        _waitingForOpponent = true;
        UpdateStatusLabel();
    }

    private void OnJoinButtonPressed()
    {
        // Show paste area for host's code
        _hostButton.Visible = false;
        _joinButton.Visible = false;
        _disconnectButton.Visible = true;

        _pasteCodeLabel.Text = "Paste host's session code:";
        _pasteCodeLabel.Visible = true;
        _sessionCodeInput.Visible = true;
        _sessionCodeInput.Text = "";
        _applyCodeButton.Text = "Join";
        _applyCodeButton.Visible = true;

        _networkStatusLabel.Text = "Paste the host's session code and click Join";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
    }

    private void OnApplyCodePressed()
    {
        string code = _sessionCodeInput.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            _networkStatusLabel.Text = "Please paste a session code first";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
            return;
        }

        var phase = _networkManager.Phase;

        if (phase == ConnectionPhase.Idle)
        {
            // Joiner applying host's code
            _networkManager.JoinGame(code);
            _networkStatusLabel.Text = "Processing host code...";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
            _sessionCodeInput.Visible = false;
            _applyCodeButton.Visible = false;
            _pasteCodeLabel.Visible = false;
        }
        else if (phase == ConnectionPhase.WaitingForAnswer)
        {
            // Host applying joiner's answer
            _networkManager.ApplyAnswerCode(code);
            _networkStatusLabel.Text = "Connecting...";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ffff88"));
            _sessionCodeInput.Visible = false;
            _applyCodeButton.Visible = false;
            _pasteCodeLabel.Visible = false;
        }
    }

    private void OnCopyCodePressed()
    {
        DisplayServer.ClipboardSet(_sessionCodeDisplay.Text);
        _copyCodeButton.Text = "Copied!";
    }

    private void OnSessionCodeReady(string code)
    {
        _sessionCodeDisplay.Text = code;
        _sessionCodeLabel.Visible = true;
        _sessionCodeDisplay.Visible = true;
        _copyCodeButton.Text = "Copy Code";
        _copyCodeButton.Visible = true;

        var phase = _networkManager.Phase;

        if (phase == ConnectionPhase.WaitingForAnswer)
        {
            // Host: show area for pasting joiner's response
            _networkStatusLabel.Text = "Send code to opponent, then paste their response below";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));

            _sessionCodeLabel.Text = "Your session code (send to opponent):";
            _pasteCodeLabel.Text = "Paste opponent's response code:";
            _pasteCodeLabel.Visible = true;
            _sessionCodeInput.Visible = true;
            _sessionCodeInput.Text = "";
            _applyCodeButton.Text = "Connect";
            _applyCodeButton.Visible = true;
        }
        else if (phase == ConnectionPhase.WaitingForConnection)
        {
            // Joiner: show their response code for host
            _networkStatusLabel.Text = "Send this response code to the host";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));

            _sessionCodeLabel.Text = "Your response code (send to host):";
        }
    }

    private void OnConnectionPhaseChanged(int phase)
    {
        var p = (ConnectionPhase)phase;
        if (p == ConnectionPhase.Connected)
        {
            // Hide signaling UI elements
            _sessionCodeLabel.Visible = false;
            _sessionCodeDisplay.Visible = false;
            _copyCodeButton.Visible = false;
            _pasteCodeLabel.Visible = false;
            _sessionCodeInput.Visible = false;
            _applyCodeButton.Visible = false;
        }
    }

    private void OnDisconnectButtonPressed()
    {
        _networkManager.Disconnect();
        ResetMultiplayerUI();
        _networkStatusLabel.Text = "Disconnected";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#888888"));
        UpdateStatusLabel();
    }

    private void ResetMultiplayerUI()
    {
        _hostButton.Visible = true;
        _joinButton.Visible = true;
        _disconnectButton.Visible = false;
        _waitingForOpponent = false;

        _sessionCodeLabel.Visible = false;
        _sessionCodeDisplay.Visible = false;
        _sessionCodeDisplay.Text = "";
        _copyCodeButton.Visible = false;
        _pasteCodeLabel.Visible = false;
        _sessionCodeInput.Visible = false;
        _sessionCodeInput.Text = "";
        _applyCodeButton.Visible = false;
    }

    private void OnNetworkConnectionSucceeded()
    {
        _networkStatusLabel.Text = "Connected! Waiting for game...";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
    }

    private void OnNetworkConnectionFailed(string reason)
    {
        _networkStatusLabel.Text = $"Connection failed: {reason}";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
        ResetMultiplayerUI();
    }

    private void OnNetworkPeerConnected(long peerId)
    {
        if (_networkManager.IsHost)
        {
            _networkStatusLabel.Text = "Opponent connected!";
            _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
            _waitingForOpponent = false;

            // Host starts the game
            StartMultiplayerGame();
        }
    }

    private void OnNetworkPeerDisconnected(long peerId)
    {
        _networkStatusLabel.Text = "Opponent disconnected";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
        _waitingForOpponent = true;

        _statusLabel.Text = "Opponent left the game";
        _statusLabel.AddThemeColorOverride("font_color", new Color("#ff6666"));
    }

    private void StartMultiplayerGame()
    {
        // Host sets up and sends the game
        _gameManager.SetupGame(_gameManager.CurrentSetupMode);
        DeselectPiece();
        ClearMoveHistory();

        // Serialize and send board state
        string boardState = NetworkManager.SerializeBoardState(_board.GetBoardState());
        _networkManager.SendGameStart(_gameManager.CurrentSetupMode, boardState);

        // Host plays as White
        _board.SetFlipped(false);

        _networkStatusLabel.Text = "Game started! You are White";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
        UpdateStatusLabel();
    }

    private void OnNetworkGameStartReceived(int setupMode, string boardState)
    {
        // Client receives the game setup
        _gameManager.CurrentSetupMode = (SetupMode)setupMode;
        _modeSelector.Selected = setupMode;
        NetworkManager.DeserializeBoardState(boardState, _board);
        _gameManager.ResetTurnState();
        DeselectPiece();
        ClearMoveHistory();

        // Client plays as Black, flip board
        _board.SetFlipped(true);

        _networkStatusLabel.Text = "Game started! You are Black";
        _networkStatusLabel.AddThemeColorOverride("font_color", new Color("#88ff88"));
        UpdateStatusLabel();
    }

    private void OnNetworkMoveRequestReceived(int fromX, int fromY, int toX, int toY, int promotionType)
    {
        // Host validates and processes the move request
        if (!_networkManager.IsHost) return;

        Vector2I from = new Vector2I(fromX, fromY);
        Vector2I to = new Vector2I(toX, toY);

        // Check if it's the client's turn (Black)
        if (_gameManager.IsWhiteTurn)
        {
            GD.Print("Rejected move: not client's turn");
            return;
        }

        // Validate the move
        var legalMoves = _gameManager.GetLegalMoves(from);
        if (!legalMoves.Contains(to))
        {
            GD.Print("Rejected move: illegal");
            return;
        }

        // Execute the move
        PieceType? promotion = promotionType >= 0 ? (PieceType)promotionType : null;

        if (_gameManager.TryMakeMove(from, to))
        {
            // Handle promotion if needed
            if (_gameManager.IsAwaitingPromotion && promotion.HasValue)
            {
                _gameManager.CompletePromotion(promotion.Value);
            }

            // Confirm the move to client
            _networkManager.ConfirmMove(from, to, promotion);

            // Check for game end
            if (_gameManager.CurrentState != GameState.Playing)
            {
                _networkManager.SendGameEnd(_gameManager.CurrentState);
            }
        }
    }

    private void OnNetworkMoveConfirmed(int fromX, int fromY, int toX, int toY, int promotionType)
    {
        // Client receives confirmed move and applies it
        if (_networkManager.IsHost) return; // Host already applied the move

        Vector2I from = new Vector2I(fromX, fromY);
        Vector2I to = new Vector2I(toX, toY);
        PieceType? promotion = promotionType >= 0 ? (PieceType)promotionType : null;

        _gameManager.TryMakeMove(from, to);

        if (_gameManager.IsAwaitingPromotion && promotion.HasValue)
        {
            _gameManager.CompletePromotion(promotion.Value);
        }

        DeselectPiece();
        UpdateStatusLabel();
    }

    private void OnNetworkGameEnded(int result)
    {
        GameState state = (GameState)result;
        OnGameOver((long)state);
    }

    private bool IsMyTurnInMultiplayer()
    {
        if (!_networkManager.IsOnline) return true;

        // Host is White, Client is Black
        bool amIWhite = _networkManager.AmIWhite;
        return _gameManager.IsWhiteTurn == amIWhite;
    }
}
