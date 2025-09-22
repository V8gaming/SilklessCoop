# SilklessCoop MrMeeseeks Fork

A cooperative multiplayer mod for Hollow Knight Silksong with enhanced networking and PvP features.

## Features

### Core Multiplayer
- **Player Synchronization**: Real-time position, animation, and compass syncing
- **Multiple Connection Types**: Steam P2P, Echo Server, and Debug modes
- **Packet-based Networking**: Structured packet system with marshalling for reliable data transmission

### Visual & Gameplay
- **Player Colors**: Full RGB color customization (format: "R,G,B")
- **Collision System**: Optional player-to-player collision with BoxCollider2D components
- **Attack Synchronization**: Complete PvP system with visual attack effects
- **Equipment Support**: Long Needle scaling and Crest imbuement effects
- **Smooth Interpolation**: Tick-based updates with configurable rates

### Technical Features
- **Harmony Patching**: Non-intrusive attack system integration
- **DummyController System**: Stores HeroController state for remote players without dependencies
- **Custom Components**: Modified NailSlash system for remote player attacks
- **Modular Architecture**: Clean separation between networking, sync, and visual systems

## Configuration

Configure via BepInEx config or in-game settings:
- `EnableCollision`: Toggle player collision (default: true)
- `EnablePvP`: Enable attack synchronization (default: true)
- `PlayerColor`: RGB color values "R,G,B" (default: "255,255,255")
- `TickRate`: Network update frequency (default: 20Hz)
- `PlayerOpacity`: Remote player transparency (default: 0.8)

## Known Issues

- Standing on another player makes the game think you're in the air still
- Disconnecting and reconnecting requires all players to reconnect together
- Some edge cases with attack animation timing

## Completed Features

- ✅ Attack Synchronization with full visual effects
- ✅ Player collision system
- ✅ RGB color customization
- ✅ Equipment state synchronization (Long Needle, Crests)
- ✅ Packet-based networking architecture
- ✅ Multiple connection backends

## Future Plans

- Pushing mechanics (to prevent ledge guarding)
- Sound synchronization (more shaw per shaw)
- Minimap indicators for other players
- Performance optimizations
- Additional equipment synchronization
