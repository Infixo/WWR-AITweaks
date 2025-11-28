# Worldwide Rush Infixo's AI Tweaks
[Worldwide Rush](https://store.steampowered.com/app/3325500/Worldwide_Rush/) mod that adds some AI enhancements to the game.

## Features
- Rewritten and redesigned line evaluation algorithm.
- More diversity in AI companies.

### Line evaluation
Better logic for evaluating and maintaining lines for the Hub Manager's Maintain mode and AI companies.
- The line is evaluated for the last 2 months and based on efficiency, throughput and vehicles' capacities, optimal range is determined.
- Optimal range is betwen 1/3 and 2/3 of minimum efficiency and full (100%) efficiency, where minimum efficiency is required for the line to be profitable at all.
- Number of waiting passengers and average trip time are also taken into consideration.
- Based on the above, the manager decides if the line should be upgraded or downgraded and/or if more/less vehicles are needed.
- Additional checks are added to make sure that last vehicle is not sold (ex. for AI).
- (0.2) All vehicle manipulations go thorugh Generated Plan which assures that a vehicle is not sold when the replacement is not possible.
- (0.2) Balanced line growth - manager will try for all vehicles be of similar tier.
- (0.2) Optimal number of vehicles accounts for trip time and type of vehicles.
- (0.2) Evaluation results can be displayed as a tooltip by UITweaks.
- (0.5) AI companies evaluate lines twice a month (instead of once).
- IMPORTANT NOTES.
  - The process of upgrading/downgrading vehicles involves first selling the old one, and after that buying a new one, same as in the vanilla gamem btw. It is imperative to make sure the hub manager has enough funds to acquire a new vehicle.
  - Only evaluation core and decision logic is changed, other supporting routines (e.g. selecting a better or worse vehicle) are still vanilla versions.

### (0.5) More diversity in AI companies
AI companies now have a chance to be
- Land-only: road vehicles + trains.
- Non-land: planes and ships.
- Any combination of 3 types.

### Troubleshooting
- Output messages are logged into AITweaksLog.txt in the %TEMP% dir. Please consult me on Discord to understand the log.

## Technical

### Requirements and Compatibility
- [WWR ModLoader](https://github.com/Infixo/WWR-ModLoader).
- [Harmony v2.4.1 for .net 8.0](https://github.com/pardeike/Harmony/releases/tag/v2.4.1.0). The correct dll is provided in the release files.

### Known Issues
- None atm.

### Changelog
- v0.5.0 (2025-11-28)
  - NEW feature. More diversity in AI companies.
  - Allows AI to buy any number of vehicles (within inventory limits).
  - AI evaluates lines twice per month (originally onl once).
  - Various small improvements in the line evaluation algorithm.
  - Compatibility with Patch 1.1.15.
  - Fixed AI behavior allowing for super long road routes and very short plane routes.
  - Fixed AI taking loans over 10M limit.
- v0.4.1 (2025-11-06)
  - Fixed waiting passengers calculations not including lines through overcrowded cities.
- v0.4.0 (2025-11-02)
  - New, much faster algorithm to calculate waiting passengers.
- v0.3.4 (2025-10-28)
  - Fixed crash when AI evaluates a line.
- v0.3.2 & v0.3.3 (2025-10-26)
  - Fixed crash from race condition when line is served by 2+ hubs.
- v0.3.1 (2025-10-25)
  - Updated for Patch 1.1.13
  - Fixed crash when downgrade net price is 0.
- v0.3.0 (2025-10-18)
  - Improved upgrading logic.
  - Improvement on how number of vehicles is calculated.
- v0.2.1 (2025-10-17)
  - Fixed a rare issue where a line has 1 vehicle and its eff & thr are 0 but not at the same month.
- v0.2.0 (2025-10-16)
  - Evaluation tooltip, rewritten optimal number of vehicles.
  - More dynamic evaluation based on 2 months data (instead of a quarter).
  - Fixed selling vehicle by a manager and getting a replacement.
  - Fixed issue with repeated generated plans.
  - Removed: Extensive logging is off.
- v0.1.0 (2025-10-11)
  - Initial release.

### Support
- Please report bugs and issues on [GitHub](https://github.com/Infixo/WWR-AITweaks).
- You may also leave comments on [Discord](https://discord.com/channels/1342565384066170964/1421898965556920342).
