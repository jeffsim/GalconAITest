using System;
using System.Collections.Generic;
using System.Linq;

public class Strategy_NonRecursive
{
    TownData SourceTownData;
    PlayerData player;
    AIAction BestAction;
    List<AI_NodeState> PlayerNodes;
    AI_TownState Town;

    public Strategy_NonRecursive(TownData townData, PlayerData player)
    {
        SourceTownData = townData;
        Town = new(player);
        Town.InitializeStaticData(townData);

        this.player = player;
        PlayerNodes = townData.Nodes.Where(node => node.OwnedBy == player).Select(node => new AI_NodeState(node)).ToList();
    }

    public AIAction DecideAction()
    {
        BestAction = new();

        CheckPriority_BuildResourceGatheringBuildings();
        // CheckPriority_UpgradeNodes();
        // CheckPriority_CaptureEmptyNodes();
        // CheckPriority_ReinforceNodes();
        // CheckPriority_AttackEnemyNodes();
        // CheckPriority_ReplaceBuildings();
        return BestAction;
    }

    private void CheckPriority_BuildResourceGatheringBuildings()
    {
        // Get a list of needed resources along with the amount needed
        var neededResources = DetermineNeededResources(player);

        // NOTE: Changing from "forest is a node next to an empty node that you build a woodcutter on"
        // to: "capture the forest node and build a woodcutter on it."  I can 'fake' the appearance of the forest
        // being nearby visually if I want.

        // For each needed resource, check if we have enough of it to meet the need
        // If not, check if we can capture a resource node so that we can
        // build a building that can gather the needed resource.  Doing so requires:
        // 1. we have the resources to build the resource-gathering building
        // 2. we own a node with enough workers that we can send to capture the resource node
        // 3. the owned node with workers has a path to the resource node
        // 3b. ... OR we can capture nodes between an owned node and the resource node
        foreach (var resourceEntry in neededResources)
        {
            // e.g.: resourceEntry.Key = wood => "I need wood, value = resourceEntry.Value"
            // Do we already have "enough" of this resource?
            // if (Town.PlayerTownInventory[resourceEntry.Key] >= resourceEntry.Value)
            // {
            //     continue;
            // }
            // // Check to see if there are any nodes that can be captured to gather the needed resource
            // foreach (var node in PlayerNodes)
            // {
            //     if (node.HasBuilding BuildingInNode.HasBuilding.BuildingInNode == null)
            //     {
            //         foreach (var neighbor in node.Neighbors)
            //         {
            //             if (neighbor.BuildingInNode == BuildingType.Forest && node.NumWorkers > 5)
            //             {
            //                 BestAction = new AIAction
            //                 {
            //                     SourceNode = node,
            //                     DestNode = neighbor,
            //                     Type = ActionType.CaptureNode,
            //                     BuildingToBuild = BuildingType.Woodcutter
            //                 };
            //                 return;
            //             }
            //         }
            //     }
            // }
        }


        // if (neededResources.ContainsKey("Wood") || neededResources.ContainsKey("Stone"))
        // {
        //     foreach (var node in PlayerNodes)
        //     {
        //         if (node.BuildingInNode == null && neededResources.Contains("Wood"))
        //         {
        //             foreach (var neighbor in node.Neighbors)
        //             {
        //                 if (neighbor.BuildingInNode == BuildingType.Forest && node.NumWorkers > 5)
        //                 {
        //                     sourceNode = node;
        //                     targetNode = neighbor;
        //                     buildingToBuild = BuildingType.Woodcutter;
        //                     return ActionType.CaptureNode;
        //                 }
        //             }
        //         }
        //         if (node.BuildingInNode == null && neededResources.Contains("Stone"))
        //         {
        //             foreach (var neighbor in node.Neighbors)
        //             {
        //                 if (neighbor.BuildingInNode == BuildingType.StoneMine && node.NumWorkers > 5)
        //                 {
        //                     sourceNode = node;
        //                     targetNode = neighbor;
        //                     buildingToBuild = BuildingType.StoneMiner;
        //                     return ActionType.CaptureNode;
        //                 }
        //             }
        //         }
        //     }
        // }
    }

    // private void CheckPriority_CaptureEmptyNodes()
    // {
    //     foreach (var node in PlayerNodes)
    //     {
    //         foreach (var neighbor in node.Neighbors)
    //         {
    //             if (neighbor.Owner == null && node.NumWorkers > 5)
    //             {
    //                 sourceNode = node;
    //                 targetNode = neighbor;
    //                 buildingToBuild = DetermineBestBuilding(neededResources, player);
    //                 if (buildingToBuild.HasValue && player.CanBuild(buildingToBuild.Value))
    //                 {
    //                     player.ConsumeResources(buildingToBuild.Value);
    //                     return ActionType.CaptureNode;
    //                 }
    //             }
    //         }
    //     }
    //     return previousBestAction;
    // }

    // private void CheckPriority_ReinforceNodes()
    // {
    //     foreach (var node in PlayerNodes)
    //     {
    //         foreach (var neighbor in node.Neighbors)
    //         {
    //             if (neighbor.OwnerId == player.Id && neighbor.NumWorkers < 10 && node.NumWorkers > 10)
    //             {
    //                 // TODO: Calculate "score" of this action and only overwrite BestAction if it's better
    //                 BestAction = new AIAction
    //                 {
    //                     SourceNode = node,
    //                     DestNode = neighbor,
    //                     Type = AIActionType.SendWorkersToOwnedNode
    //                 };
    //             }
    //         }
    //     }
    // }

    // private void CheckPriority_UpgradeNodes()
    // {
    //     foreach (var node in PlayerNodes)
    //     {
    //         if (node.NumWorkers >= node.MaxWorkers && node.MaxWorkers < 40)
    //         {
    //             sourceNode = node;
    //             return ActionType.UpgradeNode;
    //         }
    //     }
    // }

    // private void CheckPriority_AttackEnemyNodes()
    // {
    //     foreach (var node in PlayerNodes)
    //     {
    //         foreach (var neighbor in node.Neighbors)
    //         {
    //             if (neighbor.Owner != null && neighbor.Owner != player && neighbor.NumWorkers < node.NumWorkers)
    //             {
    //                 sourceNode = node;
    //                 targetNode = neighbor;
    //                 return ActionType.AttackNode;
    //             }
    //         }
    //     }
    // }

    // private void CheckPriority_ReplaceBuildings()
    // {
    //     foreach (var node in PlayerNodes)
    //     {
    //         if (node.BuildingInNode.HasValue && ShouldReplaceBuilding(node.BuildingInNode.Value, neededResources, player))
    //         {
    //             sourceNode = node;
    //             buildingToBuild = DetermineBestBuilding(neededResources, player);
    //             if (buildingToBuild.HasValue && player.CanBuild(buildingToBuild.Value))
    //             {
    //                 player.ConsumeResources(buildingToBuild.Value);
    //                 return ActionType.ReplaceBuilding;
    //             }
    //         }
    //     }
    // }

    private BuildingType? DetermineBestBuilding(List<string> neededResources, PlayerData player)
    {
        // Choose the best building to construct based on what resources are needed
        if (neededResources.Contains("Wood"))
        {
            return BuildingType.Woodcutter;
        }
        else if (neededResources.Contains("Stone"))
        {
            return BuildingType.StoneMiner;
        }

        // Additional logic can be added for advanced buildings

        return null;
    }

    private bool ShouldReplaceBuilding(BuildingType currentBuilding, List<string> neededResources, PlayerData player)
    {
        // Determine if a building should be replaced based on current needs
        if (currentBuilding == BuildingType.Woodcutter && !neededResources.Contains("Wood"))
        {
            return true;
        }
        if (currentBuilding == BuildingType.StoneMiner && !neededResources.Contains("Stone"))
        {
            return true;
        }

        return false;
    }

    private Dictionary<GoodType, float> DetermineNeededResources(PlayerData player)
    {
        var neededResources = new Dictionary<GoodType, float>();

        // Step 1: Gather data on current resource stocks
        // var currentResources = player.Resources;

        // // Step 2: Assess the importance of each resource
        // foreach (var building in player.PotentialBuildings)
        // {
        //     var requiredResources = building.ResourcesRequiredToBuild;

        //     // Step 3: Evaluate if we should prioritize resources for each building
        //     bool buildingUseful = IsBuildingUseful(building, player);

        //     if (buildingUseful)
        //     {
        //         foreach (var resource in requiredResources)
        //         {
        //             // Calculate how much more of this resource is needed
        //             float deficit = resource.Value - currentResources[resource.Key];

        //             // If there's a deficit, this resource is needed
        //             if (deficit > 0)
        //             {
        //                 if (!neededResources.ContainsKey(resource.Key))
        //                 {
        //                     neededResources[resource.Key] = 0;
        //                 }
        //                 neededResources[resource.Key] += deficit;
        //             }
        //         }
        //     }
        // }

        // Step 4: Weigh resource needs based on their role in the economy
        AdjustResourcePriorities(neededResources, player);

        return neededResources;
    }

    private void AdjustResourcePriorities(Dictionary<GoodType, float> neededResources, PlayerData player)
    {
        // Step 5: Adjust the priority of each resource based on the player’s strategic goals
        // For example, if player plans to expand military, iron might be prioritized higher

        // Example adjustment: If the player plans to upgrade nodes or build more complex economic chains
        // if (player.Strategy == PlayerStrategy.ExpandMilitary)
        // {
        //     if (neededResources.ContainsKey(GoodType.Iron))
        //     {
        //         neededResources[GoodType.Iron] *= 1.5f; // Increase priority of iron for military expansions
        //     }
        // }

        // Additional strategic adjustments can be made based on the player's current state and goals
    }

    private bool IsBuildingUseful(BuildingDefn building, PlayerData player)
    {
        // Step 1: Assess if the building is part of a critical economic chain
        if (IsPartOfCriticalChain(building, player))
        {
            return true; // Building is crucial to advance the player's economy or strategy
        }

        // Step 2: Consider the player's strategic goals
        // switch (player.Strategy)
        // {
        //     case PlayerStrategy.ExpandMilitary:
        //         if (building.Type == BuildingType.Blacksmith || building.Type == BuildingType.Weaponsmith)
        //         {
        //             return true; // Essential for military expansion
        //         }
        //         break;

        //     case PlayerStrategy.EconomicExpansion:
        //         if (building.Type == BuildingType.Woodcutter || building.Type == BuildingType.StoneMiner)
        //         {
        //             return true; // Resource buildings are key for economic growth
        //         }
        //         break;

        //     case PlayerStrategy.Defensive:
        //         if (building.Type == BuildingType.Fortification || building.Type == BuildingType.Outpost)
        //         {
        //             return true; // Important for defense-focused strategy
        //         }
        //         break;

        //     case PlayerStrategy.TechAdvancement:
        //         if (IsTechAdvancingBuilding(building))
        //         {
        //             return true; // Advances technology, unlocking new capabilities
        //         }
        //         break;

        //         // Add more strategy types as needed
        // }

        // Step 3: Evaluate resource availability and future needs
        if (WillResourceShortageHinderFuturePlans(building, player))
        {
            return false; // Building is not useful if its construction would cause resource shortages
        }

        // Step 4: Consider map control and territory influence
        // if (building.Type == BuildingType.Outpost && IsCriticalNode(player.CurrentNode))
        // {
        //     return true; // Outposts in critical nodes can expand control and influence
        // }

        // If none of the above conditions are met, the building is not considered strategically useful
        return false;
    }

    private bool IsPartOfCriticalChain(BuildingDefn building, PlayerData player)
    {
        // Check if the building is part of a crucial production chain (e.g., IronMine -> Blacksmith -> Weaponsmith)
        // var criticalChains = player.CriticalChains;

        // foreach (var chain in criticalChains)
        // {
        //     if (chain.Contains(building.Type))
        //     {
        //         return true; // Building is part of a vital economic or military production chain
        //     }
        // }

        return false;
    }

    private bool WillResourceShortageHinderFuturePlans(BuildingDefn building, PlayerData player)
    {
        // Evaluate if building this structure will consume resources needed for other critical projects
        // foreach (var resource in building.ResourcesRequiredToBuild)
        // {
        //     if (player.Resources[resource.Key] < resource.Value && !player.HasResourceIncome(resource.Key))
        //     {
        //         return true; // Future plans could be hindered by resource shortages
        //     }
        // }

        return false;
    }

    // private bool IsCriticalNode(AINode_State node)
    // {
    //     return false;
    //     // Determine if this node is strategically important (e.g., it's a chokepoint or resource-rich)
    //     // return node.IsChokepoint || node.HasRareResources;
    // }
}