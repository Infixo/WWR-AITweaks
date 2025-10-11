# Worldwide Rush Infixo's AI Tweaks
[Worldwide Rush](https://store.steampowered.com/app/3325500/Worldwide_Rush/) mod that adds some AI enhancements to the game.

## Features

### Maintain routes
Currently only one feature is implemented - better logic for evaluating lines in the Hub Manager's Maintain mode. Please consider this feature as experimental atm, in the beta testing phase. For this purpoose an extensive logging is implemented.
- The line is evaluated for the last quarter and based on efficiency, optimal range is determined.
- Optimal range is betwen 1/3 and 2/3 of minimum efficiency and full (100%) efficiency, where minimum efficiency is required for the line to be profitable at all.
- Number of waiting passengers and average trip trime is also taken into consideration.
- Based on the above, the manager decides if the line should be upgraded or downgraded and/or if more/less vehicles are needed.
- IMPORTANT NOTES.
  - The process of upgrading/downgrading involves first selling the old one, and after that buying a new one. This logic also exists in the vanilla gamem btw. It is imperative to make sure the hub manager has enough funds to acquire a new vehicle. Otherwise, you may end up with an empty line. I recommend using Budget setting and set it to the value that allows to buy the best vehicle currently available.
  - Only evaluation core and decision logic is changed, other supporting routines (e.g. selecting a better or worse vehicle) are still vanilla verions.

### Troubleshooting
- Output messages are logged into AITweaksLog.txt in the %TEMP% dir. Please consult me on Discord to understand the log.

## Technical

### Requirements and Compatibility
- [WWR ModLoader](https://github.com/Infixo/WWR-ModLoader).
- [Harmony v2.4.1 for .net 8.0](https://github.com/pardeike/Harmony/releases/tag/v2.4.1.0). The correct dll is provided in the release files.

### Known Issues
- None atm.

### Changelog
- v0.1.0 (2025-10-11)
  - Initial release.

### Support
- Please report bugs and issues on [GitHub](https://github.com/Infixo/WWR-AITweaks).
- You may also leave comments on [Discord](https://discord.com/channels/1342565384066170964/1421898965556920342).
