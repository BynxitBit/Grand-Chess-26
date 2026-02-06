# Grand Chess

A chess variant with variable board sizes (3x3 to 99x99) and multiple setup modes

## Features

- **Variable board size** (3-99 squares)
- **Multiple setup modes:**
  - Two Lines - Two ranks of pieces behind pawns (default)
  - One Line - Chess960-style randomized back rank
  - Three Lines - Dense setup with 3 ranks of pieces
  - Custom - Place pieces manually with mirroring support
- **Game modes:** Player vs Player, Player vs AI, Online Multiplayer
- **FEN import/export** for sharing positions
- **Pawn promotion** to Queen, Rook, Bishop, or Knight

## Controls

- **Left-click:** Select/move pieces
- **Right-click/Middle-drag:** Pan camera
- **Scroll:** Zoom in/out
- **R:** New game
- **F:** Flip board
- **1-4:** Quick select setup mode
- **Escape:** Deselect/cancel

## Download & Install

1. Download the zip file for your platform
2. Extract the contents to a folder of your choice
3. Run the executable:
   - **Windows:** Run `GrandChess26.exe`
   - **Linux:** Run `GrandChess26.x86_64` (you may need to mark it executable with `chmod +x`)
   - **macOS:** Run `GrandChess26.app`

### Assets: Chess pieces by [RhosGFX](https://rhosgfx.itch.io/vector-chess-pieces).

## Development

### Requirements

- Godot 4.x with .NET support

### Running from Source

Open the project in Godot and press F5 to run
