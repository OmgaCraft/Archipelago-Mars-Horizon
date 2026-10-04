from dataclasses import dataclass

from Options import Choice, DeathLinkMixin, OptionGroup, PerGameCommonOptions, Range, Toggle


class Agency(Choice):
    """Agence que tu joues. Elle détermine les technologies, fusées, payloads et locations du slot.

    Dans le jeu, choisis la même agence (nouvelle partie, scénario par défaut)."""
    display_name = "Agency"
    option_usa = 0
    option_russia = 1
    option_europe = 2
    option_china = 3
    option_japan = 4
    default = 0


class Goal(Choice):
    """Condition de victoire.

    crewed_mars_landing : réussir la mission finale (premier humain sur Mars).
    milestones : atteindre un nombre de jalons (voir Milestone Goal Count)."""
    display_name = "Goal"
    option_crewed_mars_landing = 0
    option_milestones = 1
    default = 0


class MilestoneGoalCount(Range):
    """Nombre de jalons à atteindre si le but est « milestones »."""
    display_name = "Milestone Goal Count"
    range_start = 5
    range_end = 39
    default = 20


class StartingLaunchpad(Toggle):
    """Commence avec le petit pas de tir (indispensable pour lancer quoi que ce soit).

    Désactivé, il est mélangé dans le multiworld : le début de partie peut alors être très limité
    (seules les recherches restent possibles)."""
    display_name = "Starting Launchpad"
    default = 1


class ShuffleBuildings(Toggle):
    """Mélange les permis de construction des bâtiments dans le multiworld.

    Désactivé, tous les bâtiments sont débloqués dès le départ ; leurs locations restent des checks."""
    display_name = "Shuffle Buildings"
    default = 1


class FillerFundsWeight(Range):
    """Poids des « Funding Grant » parmi les items de remplissage."""
    display_name = "Funding Grant Weight"
    range_start = 0
    range_end = 100
    default = 50


class FillerScienceWeight(Range):
    """Poids des « Research Data » parmi les items de remplissage."""
    display_name = "Research Data Weight"
    range_start = 0
    range_end = 100
    default = 30


class FillerSupportWeight(Range):
    """Poids des « Public Support » parmi les items de remplissage."""
    display_name = "Public Support Weight"
    range_start = 0
    range_end = 100
    default = 20


class FillerStrength(Range):
    """Taille des items de remplissage, en pourcentage de la valeur de base (voir la doc du jeu)."""
    display_name = "Filler Strength"
    range_start = 25
    range_end = 400
    default = 100


@dataclass
class MarsHorizonOptions(DeathLinkMixin, PerGameCommonOptions):
    agency: Agency
    goal: Goal
    milestone_goal_count: MilestoneGoalCount
    starting_launchpad: StartingLaunchpad
    shuffle_buildings: ShuffleBuildings
    filler_funds_weight: FillerFundsWeight
    filler_science_weight: FillerScienceWeight
    filler_support_weight: FillerSupportWeight
    filler_strength: FillerStrength


option_groups = [
    OptionGroup("Game", [Agency, Goal, MilestoneGoalCount, StartingLaunchpad, ShuffleBuildings]),
    OptionGroup("Filler", [FillerFundsWeight, FillerScienceWeight, FillerSupportWeight, FillerStrength]),
]
