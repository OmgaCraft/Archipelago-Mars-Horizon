# Mars Horizon

## Where is the options page?

The [player options page for this game](../player-options) contains all the options you need to configure and export a config file.

## What does randomization do to this game?

Technologies, buildings and missions are shuffled into the multiworld. Your own space agency starts with the bare
minimum (HQ, Vehicle Hangar, a sounding rocket) and unlocks everything else through items sent by other players
(or found by yourself).

Every research you complete, every building you construct for the first time and every milestone ("first satellite",
"first human in space"...) you reach is a location check.

## What items and locations get shuffled?

- **Items:** each technology node of your agency's research tree (boosters, upper stages, supplementary boosters,
  payloads, missions/destinations, buildings, building limits), plus filler: *Funding Grant* (funds), *Research Data*
  (science) and *Public Support*.
- **Locations:** completing a research, constructing a building for the first time, and reaching each space-race milestone.

## How does research work with the randomizer?

You can research any node of the tree as normal, in the order the tree allows: completing a research sends its check,
but does **not** unlock its content. The content (rocket part, payload, building permit, mission) is unlocked only
when you *receive* the matching item. Era rewards (the automatic bonuses for finishing an era of a tree) work as in the
base game.

## What is the goal?

Complete the final Mars mission (first human on Mars), or reach a number of milestones, depending on the `goal` option.

## What does another world's item look like in Mars Horizon?

Received items appear as a notification in the corner of the screen, and the technology is added to your agency.

## Logic

A mission needs: its research, a payload able to carry the required crew, a vehicle (booster + upper stage, or a
single-stage rocket) with enough range and capacity, the matching launchpad size, and, for crewed missions, the
Astronaut Training Facility. The final Mars mission also needs the two "Mars requirement" missions.
Money and time are not part of the logic.
